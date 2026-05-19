using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SlateClean.Core.CacheLocations;
using SlateClean.Core.Data;
using SlateClean.Core.Models;
using SlateClean.Core.Services;
using Xunit;

namespace SlateClean.Tests;

// async void handler timing + threadpool contention with parallel suites
// makes the SpinUntil polls flaky; serialize this class against itself.
[CollectionDefinition(nameof(AutoCleanCoordinatorTests), DisableParallelization = true)]
public class AutoCleanCoordinatorTestsCollection { }

[Collection(nameof(AutoCleanCoordinatorTests))]
public class AutoCleanCoordinatorTests : IDisposable
{
    private readonly string _root;
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<SlateCleanDbContext> _opts;

    public AutoCleanCoordinatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SlateCleanAutoTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);

        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<SlateCleanDbContext>().UseSqlite(_conn).Options;
        using var ctx = new SlateCleanDbContext(_opts);
        ctx.EnsureSchemaUpToDate();
    }

    public void Dispose()
    {
        _conn.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private SlateCleanDbContext NewContext() => new(_opts);

    private sealed class FakeLocator : ICacheLocator
    {
        public string AppName { get; init; } = "Fake";
        public required DirectoryInfo Directory { get; init; }
        public IEnumerable<DirectoryInfo> GetCacheDirectories() { yield return Directory; }
        public long GetCacheSizeBytes() => 0;
    }

    private sealed class RecordingDeleter : IFileDeleter
    {
        public List<string> Deleted { get; } = new();
        public void Delete(string path) { Deleted.Add(path); File.Delete(path); }
    }

    private sealed class StubMonitor : DiskMonitorService
    {
        public long FreeBytesValue { get; set; }
        public long TotalBytesValue { get; set; } = 500L * 1024 * 1024 * 1024;

        public StubMonitor(Func<DateTime> now, Func<AppSettings> getSettings)
            : base(
                Array.Empty<ICacheLocator>(),
                getSettings,
                NullLogger<DiskMonitorService>.Instance,
                now)
        { }

        protected override long GetFreeBytes(string driveRoot) => FreeBytesValue;
        protected override long GetTotalBytes(string driveRoot) => TotalBytesValue;
    }

    private const long Gb = 1024L * 1024 * 1024;

    private string WriteFile(string dir, string name, DateTime lastWriteUtc, int sizeBytes)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private static void SpinUntil(Func<bool> predicate, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && !predicate())
        {
            Thread.Sleep(10);
        }
    }

    // Seeds an AppSettings row through SettingsRepository (so the audit-log
    // side effects of opt-in are honoured) and returns a fresh repository
    // bound to a shared in-memory database.
    private SettingsRepository NewRepo(AppSettings seed)
    {
        var ctx = NewContext();
        var repo = new SettingsRepository(ctx);
        repo.Save(seed);
        return repo;
    }

    [Fact]
    public void Soft_breach_raises_PlanBuilt_and_does_not_execute()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var file = WriteFile(dir.FullName, "old.dat", now.AddDays(-2), 100);

        var locators = new ICacheLocator[]
        {
            new FakeLocator { AppName = "A", Directory = dir },
        };
        using var db = NewContext();
        var deleter = new RecordingDeleter();
        var cleanup = new CleanupService(locators, db,
            NullLogger<CleanupService>.Instance, deleter, () => now);

        var settings = new AppSettings { ThresholdGb = 20 };
        var monitor = new StubMonitor(() => now, () => settings)
        {
            FreeBytesValue = 5 * Gb,
        };
        var repo = NewRepo(settings);
        using var coord = new AutoCleanCoordinator(monitor, cleanup, repo,
            NullLogger<AutoCleanCoordinator>.Instance, () => now);

        CleanupPlan? planBuilt = null;
        CleanupPlan? planExecuted = null;
        coord.PlanBuilt += (_, plan) => planBuilt = plan;
        coord.PlanExecuted += (_, plan) => planExecuted = plan;

        monitor.Poll();
        SpinUntil(() => planBuilt is not null, timeoutMs: 10000);

        Assert.NotNull(planBuilt);
        Assert.Equal(BreachTier.SoftThreshold, planBuilt!.Tier);
        Assert.Null(planExecuted);                  // silent path NOT taken
        Assert.True(File.Exists(file));             // nothing deleted yet
        Assert.Empty(deleter.Deleted);
    }

    [Fact]
    public void Soft_breach_still_uses_soft_path_when_critical_opt_in_is_enabled()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var file = WriteFile(dir.FullName, "old.dat", now.AddDays(-2), 100);

        var locators = new ICacheLocator[]
        {
            new FakeLocator { AppName = "A", Directory = dir },
        };
        using var db = NewContext();
        var deleter = new RecordingDeleter();
        var cleanup = new CleanupService(locators, db,
            NullLogger<CleanupService>.Instance, deleter, () => now);

        var settings = new AppSettings
        {
            ThresholdGb = 20,
            CriticalThresholdEnabled = true,
            CriticalThresholdGb = 5,
        };
        // Free space below SOFT but above CRITICAL — must take the soft path.
        var monitor = new StubMonitor(() => now, () => settings)
        {
            FreeBytesValue = 10 * Gb,
        };
        var repo = NewRepo(settings);
        using var coord = new AutoCleanCoordinator(monitor, cleanup, repo,
            NullLogger<AutoCleanCoordinator>.Instance, () => now);

        CleanupPlan? planBuilt = null;
        CleanupPlan? planExecuted = null;
        coord.PlanBuilt += (_, plan) => planBuilt = plan;
        coord.PlanExecuted += (_, plan) => planExecuted = plan;

        monitor.Poll();
        SpinUntil(() => planBuilt is not null, timeoutMs: 10000);

        Assert.NotNull(planBuilt);
        Assert.Equal(BreachTier.SoftThreshold, planBuilt!.Tier);
        Assert.Null(planExecuted);
        Assert.True(File.Exists(file));
        Assert.Empty(deleter.Deleted);
    }

    [Fact]
    public async Task Critical_breach_with_opt_in_executes_silently_and_writes_audit_row()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var file = WriteFile(dir.FullName, "old.dat", now.AddDays(-2), 100);

        var locators = new ICacheLocator[]
        {
            new FakeLocator { AppName = "A", Directory = dir },
        };
        using var db = NewContext();
        var deleter = new RecordingDeleter();
        var cleanup = new CleanupService(locators, db,
            NullLogger<CleanupService>.Instance, deleter, () => now);

        var settings = new AppSettings
        {
            ThresholdGb = 20,
            CriticalThresholdEnabled = true,
            CriticalThresholdGb = 5,
        };
        var monitor = new StubMonitor(() => now, () => settings);
        var repo = NewRepo(settings);
        using var coord = new AutoCleanCoordinator(monitor, cleanup, repo,
            NullLogger<AutoCleanCoordinator>.Instance, () => now);

        CleanupPlan? planBuilt = null;
        CleanupPlan? planExecuted = null;
        coord.PlanBuilt += (_, plan) => planBuilt = plan;
        coord.PlanExecuted += (_, plan) => planExecuted = plan;

        const long freeAtTrigger = 3L * Gb;
        await coord.HandleBreachAsync(new DiskThresholdBreachedEventArgs(
            freeBytes: freeAtTrigger,
            thresholdBytes: 5L * Gb,
            timestampUtc: now,
            tier: BreachTier.CriticalThreshold));

        // Silent path: PlanBuilt MUST NOT fire (the tray listens on it for
        // the balloon → Review window prompt). PlanExecuted DOES fire.
        Assert.Null(planBuilt);
        Assert.NotNull(planExecuted);

        // File actually deleted via the shared ExecutePlanAsync path.
        Assert.False(File.Exists(file));
        Assert.Single(deleter.Deleted);

        // Per-file CleanupLog row exists.
        using var verify = NewContext();
        var cleanupLog = Assert.Single(verify.CleanupLogs);
        Assert.Equal("A", cleanupLog.AppName);
        Assert.Equal(1, cleanupLog.FilesDeleted);
        Assert.Equal(100, cleanupLog.BytesFreed);
        Assert.True(cleanupLog.Success);

        // SettingsAuditLog row populated with every required field.
        var audit = Assert.Single(verify.SettingsAuditLogs.Where(
            a => a.EventType == SettingsRepository.AuditEventCriticalFire));
        Assert.Equal(now, audit.TimestampUtc);
        Assert.Equal(freeAtTrigger, audit.FreeBytesAtTrigger);
        Assert.Equal(5, audit.CriticalThresholdGb);
        Assert.Equal(planExecuted!.PlanId.ToString(), audit.PlanId);
        Assert.Equal(1, audit.FilesDeleted);
        Assert.Equal(100L, audit.BytesFreed);
    }

    [Fact]
    public async Task Critical_breach_with_opt_in_disabled_is_a_no_op()
    {
        // Defense-in-depth: DiskMonitorService already gates critical-tier
        // events on the opt-in, but if a critical-tier event ever reaches
        // the coordinator with opt-in == false, it must refuse to execute.
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var file = WriteFile(dir.FullName, "old.dat", now.AddDays(-2), 100);

        var locators = new ICacheLocator[]
        {
            new FakeLocator { AppName = "A", Directory = dir },
        };
        using var db = NewContext();
        var deleter = new RecordingDeleter();
        var cleanup = new CleanupService(locators, db,
            NullLogger<CleanupService>.Instance, deleter, () => now);

        var settings = new AppSettings
        {
            ThresholdGb = 20,
            CriticalThresholdEnabled = false,         // opt-in OFF
            CriticalThresholdGb = 5,
        };
        var monitor = new StubMonitor(() => now, () => settings);
        var repo = NewRepo(settings);
        using var coord = new AutoCleanCoordinator(monitor, cleanup, repo,
            NullLogger<AutoCleanCoordinator>.Instance, () => now);

        CleanupPlan? planBuilt = null;
        CleanupPlan? planExecuted = null;
        coord.PlanBuilt += (_, plan) => planBuilt = plan;
        coord.PlanExecuted += (_, plan) => planExecuted = plan;

        await coord.HandleBreachAsync(new DiskThresholdBreachedEventArgs(
            freeBytes: 3L * Gb,
            thresholdBytes: 5L * Gb,
            timestampUtc: now,
            tier: BreachTier.CriticalThreshold));

        // No execution, no plan, no audit, file untouched.
        Assert.Null(planBuilt);
        Assert.Null(planExecuted);
        Assert.True(File.Exists(file));
        Assert.Empty(deleter.Deleted);

        using var verify = NewContext();
        Assert.Empty(verify.CleanupLogs);
        Assert.Empty(verify.SettingsAuditLogs.Where(
            a => a.EventType == SettingsRepository.AuditEventCriticalFire));
    }

    [Fact]
    public void Coordinator_unsubscribes_on_dispose()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var settings = new AppSettings { ThresholdGb = 20 };
        var monitor = new StubMonitor(() => now, () => settings)
        {
            FreeBytesValue = 5 * Gb,
        };

        using var db = NewContext();
        var cleanup = new CleanupService(
            Array.Empty<ICacheLocator>(), db,
            NullLogger<CleanupService>.Instance, new RecordingDeleter());

        var repo = NewRepo(settings);
        var coord = new AutoCleanCoordinator(monitor, cleanup, repo,
            NullLogger<AutoCleanCoordinator>.Instance, () => now);
        var fired = 0;
        coord.PlanBuilt += (_, _) => fired++;
        coord.PlanExecuted += (_, _) => fired++;

        coord.Dispose();
        monitor.Poll();

        // Give the async-void handler a moment if it were still subscribed.
        Thread.Sleep(200);
        Assert.Equal(0, fired);
    }
}

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
        ctx.Database.EnsureCreated();
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

    [Fact]
    public void Coordinator_builds_plan_with_soft_tier_on_soft_breach()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        WriteFile(dir.FullName, "old.dat", now.AddDays(-2), 100);

        var locators = new ICacheLocator[]
        {
            new FakeLocator { AppName = "A", Directory = dir },
        };
        using var db = NewContext();
        var cleanup = new CleanupService(locators, db,
            NullLogger<CleanupService>.Instance,
            new RecordingDeleter(),
            () => now);

        var settings = new AppSettings { ThresholdGb = 20 };
        var monitor = new StubMonitor(() => now, () => settings)
        {
            FreeBytesValue = 5 * Gb,
        };
        using var coord = new AutoCleanCoordinator(monitor, cleanup,
            NullLogger<AutoCleanCoordinator>.Instance);

        CleanupPlan? capturedPlan = null;
        coord.PlanBuilt += (_, plan) => capturedPlan = plan;

        monitor.Poll();
        SpinUntil(() => capturedPlan is not null, timeoutMs: 10000);

        Assert.NotNull(capturedPlan);
        Assert.Equal(BreachTier.SoftThreshold, capturedPlan!.Tier);
        Assert.Equal(1, capturedPlan.TotalFiles);
        Assert.Equal(100, capturedPlan.TotalBytes);
    }

    [Fact]
    public void Coordinator_builds_plan_with_critical_tier_when_opted_in()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        WriteFile(dir.FullName, "old.dat", now.AddDays(-2), 100);

        var locators = new ICacheLocator[]
        {
            new FakeLocator { AppName = "A", Directory = dir },
        };
        using var db = NewContext();
        var cleanup = new CleanupService(locators, db,
            NullLogger<CleanupService>.Instance,
            new RecordingDeleter(),
            () => now);

        var settings = new AppSettings
        {
            ThresholdGb = 20,
            CriticalThresholdEnabled = true,
            CriticalThresholdGb = 5,
        };
        var monitor = new StubMonitor(() => now, () => settings)
        {
            FreeBytesValue = 3 * Gb,
        };
        using var coord = new AutoCleanCoordinator(monitor, cleanup,
            NullLogger<AutoCleanCoordinator>.Instance);

        CleanupPlan? capturedPlan = null;
        coord.PlanBuilt += (_, plan) => capturedPlan = plan;

        monitor.Poll();
        SpinUntil(() => capturedPlan is not null, timeoutMs: 10000);

        Assert.NotNull(capturedPlan);
        Assert.Equal(BreachTier.CriticalThreshold, capturedPlan!.Tier);
    }

    [Fact]
    public void Coordinator_does_not_delete_or_write_to_cleanup_logs()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var oldFile = WriteFile(dir.FullName, "old.dat", now.AddDays(-2), 100);

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
        var monitor = new StubMonitor(() => now, () => settings)
        {
            FreeBytesValue = 3 * Gb,
        };
        using var coord = new AutoCleanCoordinator(monitor, cleanup,
            NullLogger<AutoCleanCoordinator>.Instance);

        var fired = false;
        coord.PlanBuilt += (_, _) => fired = true;

        monitor.Poll();
        SpinUntil(() => fired, timeoutMs: 10000);

        Assert.True(fired);
        // Safety net: even on a critical-tier breach in 6b, nothing executes.
        Assert.True(File.Exists(oldFile));
        Assert.Empty(deleter.Deleted);
        using var verify = NewContext();
        Assert.Empty(verify.CleanupLogs);
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

        var coord = new AutoCleanCoordinator(monitor, cleanup,
            NullLogger<AutoCleanCoordinator>.Instance);
        var fired = 0;
        coord.PlanBuilt += (_, _) => fired++;

        coord.Dispose();
        monitor.Poll();

        // Give the async-void handler a moment if it were still subscribed.
        Thread.Sleep(200);
        Assert.Equal(0, fired);
    }
}

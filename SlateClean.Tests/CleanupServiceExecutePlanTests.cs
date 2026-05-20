using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SlateClean.Core.CacheLocations;
using SlateClean.Core.Data;
using SlateClean.Core.Models;
using SlateClean.Core.Services;
using Xunit;

namespace SlateClean.Tests;

public class CleanupServiceExecutePlanTests : IDisposable
{
    private readonly string _root;
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<SlateCleanDbContext> _opts;

    public CleanupServiceExecutePlanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SlateCleanExecuteTests_" + Guid.NewGuid());
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

    private sealed class RecordingDeleter : IFileDeleter
    {
        public List<string> Deleted { get; } = new();
        public void Delete(string path)
        {
            Deleted.Add(path);
            File.Delete(path);
        }
    }

    private string WriteFile(string dir, string name, int sizeBytes)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private CleanupService NewService(DateTime now, IFileDeleter deleter)
    {
        return new CleanupService(
            Array.Empty<ICacheLocator>(),
            NewContext(),
            NullLogger<CleanupService>.Instance,
            deleter,
            () => now);
    }

    [Fact]
    public async Task ExecutePlan_deletes_exactly_the_files_in_the_plan()
    {
        var now = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var planFile = WriteFile(dir.FullName, "in-plan.dat", 100);
        var extraFile = WriteFile(dir.FullName, "added-after.dat", 200);

        var plan = new CleanupPlan(
            Guid.NewGuid(),
            now,
            BreachTier.SoftThreshold,
            new[]
            {
                new PlannedAppCleanup("A", new[]
                {
                    new PlannedFileDeletion(planFile, 100, now.AddDays(-2)),
                }),
            });

        var deleter = new RecordingDeleter();
        var svc = NewService(now, deleter);

        var logs = await svc.ExecutePlanAsync(plan);

        // Exactly the in-plan file was deleted; the file added after plan build
        // is untouched. This is the immutability guarantee the user reviews against.
        Assert.False(File.Exists(planFile));
        Assert.True(File.Exists(extraFile));
        Assert.Equal(new[] { planFile }, deleter.Deleted);
        Assert.Single(logs);
        Assert.True(logs[0].Success);
        Assert.Equal(1, logs[0].FilesDeleted);
        Assert.Equal(100, logs[0].BytesFreed);
    }

    [Fact]
    public async Task ExecutePlan_logs_and_skips_files_already_gone()
    {
        var now = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var keptFile = WriteFile(dir.FullName, "still-there.dat", 50);
        var ghostPath = Path.Combine(dir.FullName, "vanished.dat");

        var plan = new CleanupPlan(
            Guid.NewGuid(),
            now,
            BreachTier.SoftThreshold,
            new[]
            {
                new PlannedAppCleanup("A", new[]
                {
                    new PlannedFileDeletion(ghostPath, 999, now.AddDays(-2)),
                    new PlannedFileDeletion(keptFile, 50, now.AddDays(-2)),
                }),
            });

        // A deleter that simulates FileNotFoundException for the ghost path —
        // exactly what would happen if another process deleted the file between
        // plan-build and execute.
        var deleter = new ThrowingDeleter(ghostPath);
        var svc = NewService(now, deleter);

        var logs = await svc.ExecutePlanAsync(plan);

        Assert.True(logs[0].Success); // missing file does not poison the run
        Assert.Equal(1, logs[0].FilesDeleted);
        Assert.Equal(50, logs[0].BytesFreed);
        Assert.False(File.Exists(keptFile));
    }

    [Fact]
    public async Task ExecutePlan_writes_one_log_row_per_app()
    {
        var now = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        var dirA = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var dirB = Directory.CreateDirectory(Path.Combine(_root, "B"));
        var fa = WriteFile(dirA.FullName, "a.dat", 10);
        var fb = WriteFile(dirB.FullName, "b.dat", 20);

        var plan = new CleanupPlan(
            Guid.NewGuid(),
            now,
            BreachTier.SoftThreshold,
            new[]
            {
                new PlannedAppCleanup("A", new[]
                {
                    new PlannedFileDeletion(fa, 10, now.AddDays(-2)),
                }),
                new PlannedAppCleanup("B", new[]
                {
                    new PlannedFileDeletion(fb, 20, now.AddDays(-2)),
                }),
            });

        var svc = NewService(now, new RecordingDeleter());
        await svc.ExecutePlanAsync(plan);

        using var verify = NewContext();
        Assert.Equal(2, verify.CleanupLogs.Count());
        Assert.Contains(verify.CleanupLogs, l => l.AppName == "A" && l.FilesDeleted == 1);
        Assert.Contains(verify.CleanupLogs, l => l.AppName == "B" && l.FilesDeleted == 1);
    }

    [Fact]
    public async Task ExecutePlan_raises_CleanupCompleted()
    {
        var now = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var f = WriteFile(dir.FullName, "x.dat", 10);
        var plan = new CleanupPlan(
            Guid.NewGuid(), now, BreachTier.SoftThreshold,
            new[] { new PlannedAppCleanup("A", new[] { new PlannedFileDeletion(f, 10, now.AddDays(-2)) }) });

        var svc = NewService(now, new RecordingDeleter());
        var fired = false;
        svc.CleanupCompleted += (_, _) => fired = true;

        await svc.ExecutePlanAsync(plan);
        Assert.True(fired);
    }

    [Fact]
    public async Task ExecutePlan_stamps_SoftThreshold_TriggeredBy_on_log_rows()
    {
        var now = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "Soft"));
        var f = WriteFile(dir.FullName, "x.dat", 10);
        var plan = new CleanupPlan(
            Guid.NewGuid(), now, BreachTier.SoftThreshold,
            new[] { new PlannedAppCleanup("Soft", new[] { new PlannedFileDeletion(f, 10, now.AddDays(-2)) }) });

        var svc = NewService(now, new RecordingDeleter());
        await svc.ExecutePlanAsync(plan);

        using var verify = NewContext();
        var log = Assert.Single(verify.CleanupLogs);
        Assert.Equal("SoftThreshold", log.TriggeredBy);
    }

    [Fact]
    public async Task ExecutePlan_stamps_CriticalThreshold_TriggeredBy_on_log_rows()
    {
        var now = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "Crit"));
        var f = WriteFile(dir.FullName, "x.dat", 10);
        var plan = new CleanupPlan(
            Guid.NewGuid(), now, BreachTier.CriticalThreshold,
            new[] { new PlannedAppCleanup("Crit", new[] { new PlannedFileDeletion(f, 10, now.AddDays(-2)) }) });

        var svc = NewService(now, new RecordingDeleter());
        await svc.ExecutePlanAsync(plan);

        using var verify = NewContext();
        var log = Assert.Single(verify.CleanupLogs);
        Assert.Equal("CriticalThreshold", log.TriggeredBy);
    }

    [Fact]
    public async Task ExecutePlan_handles_empty_plan_without_error()
    {
        var now = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        var plan = new CleanupPlan(Guid.NewGuid(), now, BreachTier.SoftThreshold, Array.Empty<PlannedAppCleanup>());
        var svc = NewService(now, new RecordingDeleter());

        var logs = await svc.ExecutePlanAsync(plan);
        Assert.Empty(logs);
    }

    [Fact]
    public async Task ExecutePlan_persists_log_before_first_delete()
    {
        var now = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var f = WriteFile(dir.FullName, "x.dat", 10);
        bool logExistedAtDelete = false;
        var deleter = new CheckingDeleter(_opts, () => logExistedAtDelete = true);

        var plan = new CleanupPlan(
            Guid.NewGuid(), now, BreachTier.SoftThreshold,
            new[] { new PlannedAppCleanup("A", new[] { new PlannedFileDeletion(f, 10, now.AddDays(-2)) }) });

        var svc = NewService(now, deleter);
        await svc.ExecutePlanAsync(plan);

        Assert.True(logExistedAtDelete);
    }

    private sealed class ThrowingDeleter : IFileDeleter
    {
        private readonly string _throwForPath;
        public ThrowingDeleter(string throwForPath) { _throwForPath = throwForPath; }
        public void Delete(string path)
        {
            if (string.Equals(path, _throwForPath, StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("simulated missing", path);
            File.Delete(path);
        }
    }

    private sealed class CheckingDeleter : IFileDeleter
    {
        private readonly DbContextOptions<SlateCleanDbContext> _opts;
        private readonly Action _onFirstDelete;
        private bool _checked;
        public CheckingDeleter(DbContextOptions<SlateCleanDbContext> opts, Action onFirstDelete)
        {
            _opts = opts; _onFirstDelete = onFirstDelete;
        }
        public void Delete(string path)
        {
            if (!_checked)
            {
                _checked = true;
                using var ctx = new SlateCleanDbContext(_opts);
                if (ctx.CleanupLogs.Any()) _onFirstDelete();
            }
            File.Delete(path);
        }
    }
}

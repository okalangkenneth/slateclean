using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SlateClean.Core.CacheLocations;
using SlateClean.Core.Data;
using SlateClean.Core.Models;
using SlateClean.Core.Services;
using Xunit;

namespace SlateClean.Tests;

public class CleanupServiceBuildPlanTests : IDisposable
{
    private readonly string _root;
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<SlateCleanDbContext> _opts;

    public CleanupServiceBuildPlanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SlateCleanPlanTests_" + Guid.NewGuid());
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
        public void Delete(string path) { Deleted.Add(path); }
    }

    private string WriteFile(string dir, string name, DateTime lastWriteUtc, int sizeBytes)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private CleanupService NewService(ICacheLocator[] locators, DateTime now, IFileDeleter? deleter = null)
    {
        var db = NewContext();
        return new CleanupService(locators, db,
            NullLogger<CleanupService>.Instance,
            deleter ?? new RecordingDeleter(),
            () => now);
    }

    [Fact]
    public async Task BuildPlan_returns_empty_plan_when_no_files_exist()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var locator = new FakeLocator { AppName = "A", Directory = dir };
        var svc = NewService(new ICacheLocator[] { locator }, now);

        var plan = await svc.BuildPlanAsync(BreachTier.Manual);

        Assert.Single(plan.Apps);
        Assert.Empty(plan.Apps[0].Files);
        Assert.Equal(0, plan.TotalFiles);
        Assert.Equal(0, plan.TotalBytes);
    }

    [Fact]
    public async Task BuildPlan_includes_files_older_than_24h_only()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var oldFile = WriteFile(dir.FullName, "old.dat", now.AddDays(-2), 100);
        var recentFile = WriteFile(dir.FullName, "recent.dat", now.AddHours(-1), 200);

        var locator = new FakeLocator { AppName = "A", Directory = dir };
        var svc = NewService(new ICacheLocator[] { locator }, now);

        var plan = await svc.BuildPlanAsync(BreachTier.SoftThreshold);

        var planned = plan.Apps[0].Files;
        Assert.Single(planned);
        Assert.Equal(oldFile, planned[0].Path);
        Assert.Equal(100, planned[0].SizeBytes);
        Assert.True(File.Exists(recentFile)); // dry-run: nothing deleted
        Assert.True(File.Exists(oldFile));
    }

    [Fact]
    public async Task BuildPlan_does_not_write_to_cleanup_logs()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        WriteFile(dir.FullName, "old.dat", now.AddDays(-2), 100);

        var locator = new FakeLocator { AppName = "A", Directory = dir };
        var svc = NewService(new ICacheLocator[] { locator }, now);

        await svc.BuildPlanAsync(BreachTier.Manual);

        using var ctx = NewContext();
        Assert.Empty(ctx.CleanupLogs);
    }

    [Fact]
    public async Task BuildPlan_aggregates_across_locators()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dirA = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var dirB = Directory.CreateDirectory(Path.Combine(_root, "B"));
        WriteFile(dirA.FullName, "a1.dat", now.AddDays(-2), 100);
        WriteFile(dirA.FullName, "a2.dat", now.AddDays(-2), 50);
        WriteFile(dirB.FullName, "b1.dat", now.AddDays(-2), 200);

        var locators = new ICacheLocator[]
        {
            new FakeLocator { AppName = "A", Directory = dirA },
            new FakeLocator { AppName = "B", Directory = dirB },
        };
        var svc = NewService(locators, now);

        var plan = await svc.BuildPlanAsync(BreachTier.CriticalThreshold);

        Assert.Equal(2, plan.Apps.Count);
        Assert.Equal(3, plan.TotalFiles);
        Assert.Equal(350, plan.TotalBytes);
        Assert.Equal(2, plan.Apps.Single(a => a.AppName == "A").Files.Count);
        Assert.Single(plan.Apps.Single(a => a.AppName == "B").Files);
    }

    [Fact]
    public async Task BuildPlan_stamps_tier_and_unique_plan_id()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var locator = new FakeLocator { AppName = "A", Directory = dir };
        var svc = NewService(new ICacheLocator[] { locator }, now);

        var p1 = await svc.BuildPlanAsync(BreachTier.SoftThreshold);
        var p2 = await svc.BuildPlanAsync(BreachTier.CriticalThreshold);

        Assert.Equal(BreachTier.SoftThreshold, p1.Tier);
        Assert.Equal(BreachTier.CriticalThreshold, p2.Tier);
        Assert.NotEqual(Guid.Empty, p1.PlanId);
        Assert.NotEqual(p1.PlanId, p2.PlanId);
        Assert.Equal(now, p1.BuiltAtUtc);
    }

    [Fact]
    public async Task BuildPlan_skips_missing_cache_directories()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var ghostDir = new DirectoryInfo(Path.Combine(_root, "ghost-does-not-exist"));
        var locator = new FakeLocator { AppName = "Ghost", Directory = ghostDir };
        var svc = NewService(new ICacheLocator[] { locator }, now);

        var plan = await svc.BuildPlanAsync(BreachTier.Manual);

        Assert.Single(plan.Apps);
        Assert.Empty(plan.Apps[0].Files);
    }
}

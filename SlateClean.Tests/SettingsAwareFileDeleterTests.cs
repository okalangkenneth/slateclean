using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SlateClean.Core.CacheLocations;
using SlateClean.Core.Data;
using SlateClean.Core.Models;
using SlateClean.Core.Services;
using Xunit;

namespace SlateClean.Tests;

public class SettingsAwareFileDeleterTests
{
    private sealed class RecordingDeleter : IFileDeleter
    {
        public List<string> Deleted { get; } = new();
        public void Delete(string path) => Deleted.Add(path);
    }

    [Fact]
    public void Delete_with_SendToRecycleBin_false_dispatches_to_permanent_deleter()
    {
        var permanent = new RecordingDeleter();
        var recycleBin = new RecordingDeleter();
        var settings = new AppSettings { SendToRecycleBin = false };
        var dispatcher = new SettingsAwareFileDeleter(permanent, recycleBin, () => settings);

        dispatcher.Delete(@"C:\fake\cache\one.dat");
        dispatcher.Delete(@"C:\fake\cache\two.dat");

        Assert.Equal(new[] { @"C:\fake\cache\one.dat", @"C:\fake\cache\two.dat" }, permanent.Deleted);
        Assert.Empty(recycleBin.Deleted);
    }

    [Fact]
    public void Delete_with_SendToRecycleBin_true_dispatches_to_recycle_bin_deleter()
    {
        var permanent = new RecordingDeleter();
        var recycleBin = new RecordingDeleter();
        var settings = new AppSettings { SendToRecycleBin = true };
        var dispatcher = new SettingsAwareFileDeleter(permanent, recycleBin, () => settings);

        dispatcher.Delete(@"C:\fake\cache\one.dat");

        Assert.Empty(permanent.Deleted);
        Assert.Equal(new[] { @"C:\fake\cache\one.dat" }, recycleBin.Deleted);
    }

    [Fact]
    public void Delete_re_reads_settings_on_every_call()
    {
        // Settings change must apply on the next deletion without restart —
        // matches DiskMonitorService's Func<AppSettings> rebinding pattern.
        var permanent = new RecordingDeleter();
        var recycleBin = new RecordingDeleter();
        var settings = new AppSettings { SendToRecycleBin = false };
        var dispatcher = new SettingsAwareFileDeleter(permanent, recycleBin, () => settings);

        dispatcher.Delete(@"C:\a.dat");          // permanent
        settings.SendToRecycleBin = true;
        dispatcher.Delete(@"C:\b.dat");          // recycle
        settings.SendToRecycleBin = false;
        dispatcher.Delete(@"C:\c.dat");          // permanent again

        Assert.Equal(new[] { @"C:\a.dat", @"C:\c.dat" }, permanent.Deleted);
        Assert.Equal(new[] { @"C:\b.dat" }, recycleBin.Deleted);
    }

    [Fact]
    public async Task ExecutePlan_through_dispatcher_writes_cleanup_log_regardless_of_selected_deleter()
    {
        // CleanupService doesn't know which physical deleter ran — the
        // per-app CleanupLog row must land either way. Run the same plan
        // twice (recycle then permanent) and assert both writes succeed.
        var root = Path.Combine(Path.GetTempPath(), "SlateCleanDispatcherTests_" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            var opts = new DbContextOptionsBuilder<SlateCleanDbContext>().UseSqlite(conn).Options;
            using (var ctx = new SlateCleanDbContext(opts)) ctx.Database.EnsureCreated();

            var fileA = Path.Combine(root, "a.dat");
            var fileB = Path.Combine(root, "b.dat");
            File.WriteAllBytes(fileA, new byte[100]);
            File.WriteAllBytes(fileB, new byte[200]);

            var permanent = new RecordingDeleter();
            var recycleBin = new RecordingDeleter();
            var settings = new AppSettings { SendToRecycleBin = true };
            var dispatcher = new SettingsAwareFileDeleter(permanent, recycleBin, () => settings);

            var now = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);
            var svcCtx = new SlateCleanDbContext(opts);
            var svc = new CleanupService(
                Array.Empty<ICacheLocator>(),
                svcCtx,
                NullLogger<CleanupService>.Instance,
                dispatcher,
                () => now);

            var planA = new CleanupPlan(
                Guid.NewGuid(), now, BreachTier.CriticalThreshold,
                new[] { new PlannedAppCleanup("AppX", new[] {
                    new PlannedFileDeletion(fileA, 100, now.AddDays(-2)) }) });

            var logsA = await svc.ExecutePlanAsync(planA);
            Assert.Single(logsA);
            Assert.True(logsA[0].Success);
            Assert.Equal(1, logsA[0].FilesDeleted);
            Assert.Equal(new[] { fileA }, recycleBin.Deleted);
            Assert.Empty(permanent.Deleted);

            // Flip the setting; same service, same dispatcher.
            settings.SendToRecycleBin = false;

            var planB = new CleanupPlan(
                Guid.NewGuid(), now, BreachTier.SoftThreshold,
                new[] { new PlannedAppCleanup("AppX", new[] {
                    new PlannedFileDeletion(fileB, 200, now.AddDays(-2)) }) });

            var logsB = await svc.ExecutePlanAsync(planB);
            Assert.Single(logsB);
            Assert.True(logsB[0].Success);
            Assert.Equal(1, logsB[0].FilesDeleted);
            Assert.Equal(new[] { fileB }, permanent.Deleted);
            Assert.Single(recycleBin.Deleted);     // unchanged from before

            // Both CleanupLog rows persisted regardless of which side fired.
            using var verify = new SlateCleanDbContext(opts);
            var rows = verify.CleanupLogs.OrderBy(l => l.Id).ToList();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.True(r.Success));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

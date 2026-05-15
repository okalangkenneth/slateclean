using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SlateClean.Core.Data;
using SlateClean.Core.Models;
using Xunit;

namespace SlateClean.Tests;

public class SlateCleanDbContextTests
{
    private static (SqliteConnection conn, DbContextOptions<SlateCleanDbContext> opts) NewInMemory()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var opts = new DbContextOptionsBuilder<SlateCleanDbContext>()
            .UseSqlite(conn)
            .Options;
        using var ctx = new SlateCleanDbContext(opts);
        ctx.Database.EnsureCreated();
        return (conn, opts);
    }

    [Fact]
    public void CleanupLog_CanBeSavedAndRetrieved()
    {
        var (conn, opts) = NewInMemory();
        using var _ = conn;

        var entry = new CleanupLog
        {
            AppName = "DaVinci Resolve",
            DirectoryPath = @"C:\test\cache",
            FilesDeleted = 42,
            BytesFreed = 1_073_741_824,
            TimestampUtc = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc),
            Success = true,
            ErrorMessage = null,
        };

        using (var ctx = new SlateCleanDbContext(opts))
        {
            ctx.CleanupLogs.Add(entry);
            ctx.SaveChanges();
        }

        using (var ctx = new SlateCleanDbContext(opts))
        {
            var loaded = ctx.CleanupLogs.Single();
            Assert.Equal("DaVinci Resolve", loaded.AppName);
            Assert.Equal(@"C:\test\cache", loaded.DirectoryPath);
            Assert.Equal(42, loaded.FilesDeleted);
            Assert.Equal(1_073_741_824, loaded.BytesFreed);
            Assert.True(loaded.Success);
            Assert.Null(loaded.ErrorMessage);
        }
    }

    [Fact]
    public void AppSettings_DefaultsArePersisted()
    {
        var (conn, opts) = NewInMemory();
        using var _ = conn;

        using (var ctx = new SlateCleanDbContext(opts))
        {
            ctx.Settings.Add(new AppSettings());
            ctx.SaveChanges();
        }

        using (var ctx = new SlateCleanDbContext(opts))
        {
            var s = ctx.Settings.Single();
            Assert.Equal(20, s.ThresholdGb);
            Assert.False(s.AutoCleanEnabled);
            Assert.False(s.RunOnStartup);
            Assert.Null(s.LastCleanedUtc);
        }
    }

    [Fact]
    public void DefaultDatabasePath_IsUnderLocalAppData()
    {
        var path = SlateCleanDbContext.DefaultDatabasePath;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(localAppData, path);
        Assert.EndsWith("slateclean.db", path);
    }
}

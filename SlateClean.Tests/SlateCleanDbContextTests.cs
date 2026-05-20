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
    public void EnsureSchemaUpToDate_adds_SnoozedUntilUtc_to_an_existing_DB_without_data_loss()
    {
        // Simulate an upgraded install: build a Settings table with the
        // pre-6e column set (no SnoozedUntilUtc), seed a row that captures
        // the user's other preferences, run the migration, then assert the
        // new column is present AND the prior data survives.
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE Settings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                    ThresholdGb INTEGER NOT NULL DEFAULT 20,
                    AutoCleanEnabled INTEGER NOT NULL DEFAULT 0,
                    RunOnStartup INTEGER NOT NULL DEFAULT 0,
                    LastCleanedUtc TEXT NULL,
                    SendToRecycleBin INTEGER NOT NULL DEFAULT 0,
                    CriticalThresholdEnabled INTEGER NOT NULL DEFAULT 0,
                    CriticalThresholdGb INTEGER NOT NULL DEFAULT 5
                );
                INSERT INTO Settings (ThresholdGb, CriticalThresholdEnabled, CriticalThresholdGb)
                VALUES (50, 1, 7);";
            cmd.ExecuteNonQuery();
        }

        var opts = new DbContextOptionsBuilder<SlateCleanDbContext>().UseSqlite(conn).Options;
        using (var ctx = new SlateCleanDbContext(opts))
        {
            ctx.EnsureSchemaUpToDate();
        }

        using (var ctx = new SlateCleanDbContext(opts))
        {
            var s = ctx.Settings.Single();
            Assert.Equal(50, s.ThresholdGb);                 // prior data intact
            Assert.True(s.CriticalThresholdEnabled);
            Assert.Equal(7, s.CriticalThresholdGb);
            Assert.Null(s.SnoozedUntilUtc);                  // new column, NULL default
        }
    }

    [Fact]
    public void EnsureSchemaUpToDate_adds_TriggeredBy_to_existing_CleanupLog_rows_as_Manual()
    {
        // Simulate an upgraded install whose CleanupLogs table predates 6f:
        // the table is missing TriggeredBy, but it already holds rows from
        // Clear Now clicks. Migration must add the column AND backfill the
        // existing rows to "Manual" (historical truth — every cleanup
        // before 6c was a manual click).
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE CleanupLogs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                    AppName TEXT NOT NULL,
                    DirectoryPath TEXT NOT NULL,
                    FilesDeleted INTEGER NOT NULL,
                    BytesFreed INTEGER NOT NULL,
                    TimestampUtc TEXT NOT NULL,
                    Success INTEGER NOT NULL,
                    ErrorMessage TEXT NULL
                );
                INSERT INTO CleanupLogs (AppName, DirectoryPath, FilesDeleted, BytesFreed, TimestampUtc, Success)
                VALUES ('DaVinci Resolve', 'C:\old\path', 7, 12345, '2026-05-01T09:00:00Z', 1),
                       ('Premiere Pro',    'C:\old\p',    3,   500, '2026-05-02T10:00:00Z', 1);";
            cmd.ExecuteNonQuery();
        }

        var opts = new DbContextOptionsBuilder<SlateCleanDbContext>().UseSqlite(conn).Options;
        using (var ctx = new SlateCleanDbContext(opts))
        {
            ctx.EnsureSchemaUpToDate();
        }

        using (var ctx = new SlateCleanDbContext(opts))
        {
            var rows = ctx.CleanupLogs.OrderBy(l => l.Id).ToList();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal("Manual", r.TriggeredBy));
            // Prior data intact:
            Assert.Equal("DaVinci Resolve", rows[0].AppName);
            Assert.Equal(7, rows[0].FilesDeleted);
            Assert.Equal(12345L, rows[0].BytesFreed);
            Assert.True(rows[0].Success);
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

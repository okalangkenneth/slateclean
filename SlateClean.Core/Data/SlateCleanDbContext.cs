using System.Data;
using Microsoft.EntityFrameworkCore;
using SlateClean.Core.Models;

namespace SlateClean.Core.Data;

public class SlateCleanDbContext : DbContext
{
    public DbSet<CleanupLog> CleanupLogs => Set<CleanupLog>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<SettingsAuditLog> SettingsAuditLogs => Set<SettingsAuditLog>();

    public SlateCleanDbContext() { }

    public SlateCleanDbContext(DbContextOptions<SlateCleanDbContext> options) : base(options) { }

    public static string DefaultDatabasePath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "SlateClean", "slateclean.db");
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured) return;

        var path = DefaultDatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        optionsBuilder.UseSqlite($"Data Source={path}");
    }

    // Idempotent schema upgrade for databases created by earlier slices.
    // EnsureCreated() creates tables when absent but never adds columns to
    // tables that already exist; the helpers below close that gap with
    // ALTER TABLE / CREATE TABLE IF NOT EXISTS so users keep their history
    // across feature additions without manual file deletion.
    public void EnsureSchemaUpToDate()
    {
        Database.EnsureCreated();

        AddColumnIfMissing("Settings", "SendToRecycleBin", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing("Settings", "CriticalThresholdEnabled", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing("Settings", "CriticalThresholdGb", "INTEGER NOT NULL DEFAULT 5");

        Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS SettingsAuditLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                TimestampUtc TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Details TEXT NULL
            );");

        // Slice 6d — extend the audit table with CriticalFire-specific columns.
        // Existing opt-in rows leave these NULL; CriticalFire rows populate all.
        AddColumnIfMissing("SettingsAuditLogs", "FreeBytesAtTrigger", "INTEGER NULL");
        AddColumnIfMissing("SettingsAuditLogs", "CriticalThresholdGb", "INTEGER NULL");
        AddColumnIfMissing("SettingsAuditLogs", "PlanId", "TEXT NULL");
        AddColumnIfMissing("SettingsAuditLogs", "FilesDeleted", "INTEGER NULL");
        AddColumnIfMissing("SettingsAuditLogs", "BytesFreed", "INTEGER NULL");
    }

    private void AddColumnIfMissing(string table, string column, string typeDecl)
    {
        var conn = Database.GetDbConnection();
        bool opened = conn.State != ConnectionState.Open;
        if (opened) conn.Open();
        try
        {
            using var info = conn.CreateCommand();
            info.CommandText = $"PRAGMA table_info({table});";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                var name = reader["name"] as string;
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            reader.Close();

            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {typeDecl};";
            alter.ExecuteNonQuery();
        }
        finally
        {
            if (opened) conn.Close();
        }
    }
}

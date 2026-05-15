using Microsoft.EntityFrameworkCore;
using SlateClean.Core.Models;

namespace SlateClean.Core.Data;

public class SlateCleanDbContext : DbContext
{
    public DbSet<CleanupLog> CleanupLogs => Set<CleanupLog>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

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
}

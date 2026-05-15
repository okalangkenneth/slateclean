using Microsoft.EntityFrameworkCore;
using SlateClean.Core.Models;

namespace SlateClean.Core.Data;

public class SlateCleanDbContext : DbContext
{
    public DbSet<CleanupLog> CleanupLogs => Set<CleanupLog>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    public SlateCleanDbContext(DbContextOptions<SlateCleanDbContext> options) : base(options) { }
}

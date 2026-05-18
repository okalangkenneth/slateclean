using Microsoft.EntityFrameworkCore;
using SlateClean.Core.Data;
using SlateClean.Core.Models;

namespace SlateClean.Core.Services;

// Read-only access to CleanupLog entries for display. Writes go through
// CleanupService, which owns the actual cleanup transaction.
public class CleanupHistoryService
{
    private readonly SlateCleanDbContext _db;

    public CleanupHistoryService(SlateCleanDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CleanupLog>> GetRecentAsync(int limit = 50)
    {
        if (limit <= 0) return Array.Empty<CleanupLog>();

        return await _db.CleanupLogs
            .OrderByDescending(l => l.TimestampUtc)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync();
    }
}

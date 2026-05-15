using Microsoft.Extensions.Logging;
using SlateClean.Core.Data;
using SlateClean.Core.Models;

namespace SlateClean.Core.Services;

public class CleanupLogger
{
    private readonly ILogger<CleanupLogger> _logger;
    private readonly SlateCleanDbContext _db;

    public CleanupLogger(ILogger<CleanupLogger> logger, SlateCleanDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    public async Task RecordAsync(CleanupLog entry, CancellationToken ct = default)
    {
        _db.CleanupLogs.Add(entry);
        await _db.SaveChangesAsync(ct);
    }
}

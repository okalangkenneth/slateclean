using Microsoft.Extensions.Logging;
using SlateClean.Core.CacheLocations;
using SlateClean.Core.Data;
using SlateClean.Core.Models;

namespace SlateClean.Core.Services;

public class CleanupService
{
    private static readonly TimeSpan MinFileAge = TimeSpan.FromHours(24);

    private readonly ICacheLocator[] _locators;
    private readonly SlateCleanDbContext _db;
    private readonly ILogger<CleanupService> _logger;
    private readonly IFileDeleter _deleter;
    private readonly Func<DateTime> _utcNow;

    public CleanupService(
        ICacheLocator[] locators,
        SlateCleanDbContext db,
        ILogger<CleanupService> logger,
        IFileDeleter? deleter = null,
        Func<DateTime>? utcNow = null)
    {
        _locators = locators;
        _db = db;
        _logger = logger;
        _deleter = deleter ?? new FileSystemDeleter();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public async Task<CleanupLog> CleanAsync(ICacheLocator locator)
    {
        var directories = locator.GetCacheDirectories().ToList();

        var log = new CleanupLog
        {
            AppName = locator.AppName,
            DirectoryPath = string.Join(";", directories.Select(d => d.FullName)),
            TimestampUtc = _utcNow(),
            FilesDeleted = 0,
            BytesFreed = 0,
            Success = false,
        };

        _db.CleanupLogs.Add(log);
        await _db.SaveChangesAsync();

        try
        {
            var planned = EnumerateEligibleFiles(locator, _utcNow());
            int filesDeleted = 0;
            long bytesFreed = 0;

            foreach (var pf in planned)
            {
                _deleter.Delete(pf.Path);
                filesDeleted++;
                bytesFreed += pf.SizeBytes;
                _logger.LogInformation(
                    "[Cleanup] {App}: deleted {Path} ({Bytes} bytes)",
                    locator.AppName, pf.Path, pf.SizeBytes);
            }

            log.FilesDeleted = filesDeleted;
            log.BytesFreed = bytesFreed;
            log.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cleanup] {App}: failed", locator.AppName);
            log.Success = false;
            log.ErrorMessage = ex.Message;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cleanup] {App}: failed to persist log update", locator.AppName);
        }

        return log;
    }

    // Raised after a CleanAllAsync run completes. Subscribers can refresh
    // history views without polling the DB.
    public event EventHandler? CleanupCompleted;

    public async Task<IEnumerable<CleanupLog>> CleanAllAsync()
    {
        var results = new List<CleanupLog>(_locators.Length);
        foreach (var locator in _locators)
        {
            results.Add(await CleanAsync(locator));
        }
        CleanupCompleted?.Invoke(this, EventArgs.Empty);
        return results;
    }

    // Dry-run preview shared with the executor — same predicate, no deletion,
    // no DB writes. The Review UI binds against the returned plan; the
    // auto-clean coordinator uses it to decide whether to prompt or silently
    // execute. PlanId stamps each build so logs can later cross-reference.
    public Task<CleanupPlan> BuildPlanAsync(BreachTier tier)
    {
        return Task.Run(() =>
        {
            var now = _utcNow();
            var apps = new List<PlannedAppCleanup>(_locators.Length);
            foreach (var locator in _locators)
            {
                var files = EnumerateEligibleFiles(locator, now);
                apps.Add(new PlannedAppCleanup(locator.AppName, files));
            }
            return new CleanupPlan(Guid.NewGuid(), now, tier, apps);
        });
    }

    // The single source of truth for "is this file eligible for deletion".
    // Both BuildPlanAsync (preview) and CleanAsync (execute) flow through
    // here, so the dry-run shown to the user is exactly what gets deleted.
    // Encodes the non-negotiable safety rules:
    //   - file must live under a known cache directory root
    //   - file must not have been modified in the last 24 hours
    private IReadOnlyList<PlannedFileDeletion> EnumerateEligibleFiles(
        ICacheLocator locator, DateTime utcNow)
    {
        var directories = locator.GetCacheDirectories().ToList();
        var allowedRoots = directories
            .Select(d => NormalizeRoot(d.FullName))
            .ToArray();
        var cutoffUtc = utcNow - MinFileAge;
        var result = new List<PlannedFileDeletion>();

        foreach (var dir in directories)
        {
            if (!dir.Exists)
            {
                _logger.LogDebug("[Cleanup] {App}: directory missing, skipping {Dir}",
                    locator.AppName, dir.FullName);
                continue;
            }

            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                var fullPath = file.FullName;

                if (!allowedRoots.Any(r =>
                    fullPath.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning(
                        "[Cleanup] {App}: refusing path outside cache roots: {Path}",
                        locator.AppName, fullPath);
                    continue;
                }

                if (file.LastWriteTimeUtc > cutoffUtc)
                {
                    _logger.LogDebug(
                        "[Cleanup] {App}: skipping recently modified file: {Path}",
                        locator.AppName, fullPath);
                    continue;
                }

                long size = 0;
                try { size = file.Length; }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[Cleanup] Could not read size of {Path}", fullPath);
                }

                result.Add(new PlannedFileDeletion(fullPath, size, file.LastWriteTimeUtc));
            }
        }
        return result;
    }

    private static string NormalizeRoot(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed + Path.DirectorySeparatorChar;
    }
}

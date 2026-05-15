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
            var allowedRoots = directories
                .Select(d => NormalizeRoot(d.FullName))
                .ToArray();

            var cutoffUtc = _utcNow() - MinFileAge;
            int filesDeleted = 0;
            long bytesFreed = 0;

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

                    _deleter.Delete(fullPath);
                    filesDeleted++;
                    bytesFreed += size;
                    _logger.LogInformation(
                        "[Cleanup] {App}: deleted {Path} ({Bytes} bytes)",
                        locator.AppName, fullPath, size);
                }
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

    public async Task<IEnumerable<CleanupLog>> CleanAllAsync()
    {
        var results = new List<CleanupLog>(_locators.Length);
        foreach (var locator in _locators)
        {
            results.Add(await CleanAsync(locator));
        }
        return results;
    }

    private static string NormalizeRoot(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed + Path.DirectorySeparatorChar;
    }
}

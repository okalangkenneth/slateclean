using Microsoft.Extensions.Logging;
using SlateClean.Core.CacheLocations;

namespace SlateClean.Core.Services;

public class CleanupService
{
    private readonly ILogger<CleanupService> _logger;
    private readonly CleanupLogger _cleanupLogger;
    private readonly IEnumerable<ICacheLocator> _locators;

    public CleanupService(
        ILogger<CleanupService> logger,
        CleanupLogger cleanupLogger,
        IEnumerable<ICacheLocator> locators)
    {
        _logger = logger;
        _cleanupLogger = cleanupLogger;
        _locators = locators;
    }
}

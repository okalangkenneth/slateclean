using SlateClean.Core.CacheLocations;

namespace SlateClean.Core.Services;

public class CacheLocator
{
    private readonly IReadOnlyList<ICacheLocator> _locators;

    public CacheLocator() : this(new ICacheLocator[]
    {
        new DaVinciCacheLocator(),
        new PremiereCacheLocator(),
        new AfterEffectsCacheLocator(),
    })
    { }

    public CacheLocator(IEnumerable<ICacheLocator> locators)
    {
        _locators = locators.ToList();
    }

    public IReadOnlyList<ICacheLocator> Locators => _locators;

    public IEnumerable<DirectoryInfo> GetAllCacheDirectories() =>
        _locators.SelectMany(l => l.GetCacheDirectories());

    public long GetTotalCacheSizeBytes() =>
        _locators.Sum(l => l.GetCacheSizeBytes());

    public IReadOnlyDictionary<string, long> GetCacheSizesByApp() =>
        _locators.ToDictionary(l => l.AppName, l => l.GetCacheSizeBytes());
}

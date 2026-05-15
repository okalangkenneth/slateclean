namespace SlateClean.Core.CacheLocations;

public interface ICacheLocator
{
    string AppName { get; }
    IEnumerable<DirectoryInfo> GetCacheDirectories();
    long GetCacheSizeBytes();
}

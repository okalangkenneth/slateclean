namespace SlateClean.Core.CacheLocations;

public abstract class CacheLocatorBase : ICacheLocator
{
    public abstract string AppName { get; }

    public abstract IEnumerable<DirectoryInfo> GetCacheDirectories();

    public long GetCacheSizeBytes()
    {
        long total = 0;
        foreach (var dir in GetCacheDirectories())
        {
            if (!dir.Exists) continue;
            total += DirectorySize(dir);
        }
        return total;
    }

    private static long DirectorySize(DirectoryInfo dir)
    {
        long size = 0;
        try
        {
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { size += file.Length; }
                catch { /* file vanished or access denied — skip */ }
            }
        }
        catch
        {
            // directory enumeration failed — return what we have
        }
        return size;
    }
}

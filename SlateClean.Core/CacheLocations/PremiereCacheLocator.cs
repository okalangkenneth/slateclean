namespace SlateClean.Core.CacheLocations;

public class PremiereCacheLocator : CacheLocatorBase
{
    public override string AppName => "Premiere Pro";

    public override IEnumerable<DirectoryInfo> GetCacheDirectories()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return new DirectoryInfo(Path.Combine(appData, "Adobe", "Common", "Media Cache Files"));
        yield return new DirectoryInfo(Path.Combine(appData, "Adobe", "Common", "Media Cache"));
    }
}

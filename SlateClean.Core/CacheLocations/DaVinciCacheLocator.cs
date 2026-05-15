namespace SlateClean.Core.CacheLocations;

public class DaVinciCacheLocator : CacheLocatorBase
{
    public override string AppName => "DaVinci Resolve";

    public override IEnumerable<DirectoryInfo> GetCacheDirectories()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return new DirectoryInfo(Path.Combine(
            appData, "Blackmagic Design", "DaVinci Resolve", "CacheClip"));
    }
}

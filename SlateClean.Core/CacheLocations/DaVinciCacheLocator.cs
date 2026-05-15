using SlateClean.Core.Models;

namespace SlateClean.Core.CacheLocations;

public class DaVinciCacheLocator : ICacheLocator
{
    public string AppName => "DaVinci Resolve";

    public IEnumerable<CacheLocation> Locate()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var path = Path.Combine(appData, "Blackmagic Design", "DaVinci Resolve", "CacheClip");
        yield return new CacheLocation
        {
            AppName = AppName,
            Path = path,
            Exists = Directory.Exists(path),
        };
    }
}

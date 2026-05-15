using SlateClean.Core.Models;

namespace SlateClean.Core.CacheLocations;

public class AfterEffectsCacheLocator : ICacheLocator
{
    public string AppName => "After Effects";

    public IEnumerable<CacheLocation> Locate()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var sharedCache = Path.Combine(appData, "Adobe", "Common", "Media Cache Files");
        yield return new CacheLocation
        {
            AppName = AppName,
            Path = sharedCache,
            Exists = Directory.Exists(sharedCache),
        };

        var aeRoot = Path.Combine(localAppData, "Adobe", "After Effects");
        if (Directory.Exists(aeRoot))
        {
            foreach (var versionDir in Directory.EnumerateDirectories(aeRoot))
            {
                var diskCache = Path.Combine(versionDir, "disk cache");
                yield return new CacheLocation
                {
                    AppName = AppName,
                    Path = diskCache,
                    Exists = Directory.Exists(diskCache),
                };
            }
        }
    }
}

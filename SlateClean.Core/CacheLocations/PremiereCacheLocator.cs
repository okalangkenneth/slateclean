using SlateClean.Core.Models;

namespace SlateClean.Core.CacheLocations;

public class PremiereCacheLocator : ICacheLocator
{
    public string AppName => "Premiere Pro";

    public IEnumerable<CacheLocation> Locate()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var sub in new[] { "Media Cache Files", "Media Cache" })
        {
            var path = Path.Combine(appData, "Adobe", "Common", sub);
            yield return new CacheLocation
            {
                AppName = AppName,
                Path = path,
                Exists = Directory.Exists(path),
            };
        }
    }
}

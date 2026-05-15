namespace SlateClean.Core.CacheLocations;

public class AfterEffectsCacheLocator : CacheLocatorBase
{
    public override string AppName => "After Effects";

    public override IEnumerable<DirectoryInfo> GetCacheDirectories()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        yield return new DirectoryInfo(Path.Combine(appData, "Adobe", "Common", "Media Cache Files"));

        var aeRoot = new DirectoryInfo(Path.Combine(localAppData, "Adobe", "After Effects"));
        if (aeRoot.Exists)
        {
            foreach (var versionDir in aeRoot.EnumerateDirectories())
            {
                yield return new DirectoryInfo(Path.Combine(versionDir.FullName, "disk cache"));
            }
        }
    }
}

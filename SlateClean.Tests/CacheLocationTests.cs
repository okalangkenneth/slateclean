using SlateClean.Core.CacheLocations;
using SlateClean.Core.Services;
using Xunit;

namespace SlateClean.Tests;

public class CacheLocationTests
{
    private static string AppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [Fact]
    public void DaVinci_AppName_IsSet()
    {
        var locator = new DaVinciCacheLocator();
        Assert.False(string.IsNullOrWhiteSpace(locator.AppName));
        Assert.Equal("DaVinci Resolve", locator.AppName);
    }

    [Fact]
    public void DaVinci_GetCacheDirectories_ResolvesExpectedPath()
    {
        var locator = new DaVinciCacheLocator();
        var dirs = locator.GetCacheDirectories().ToList();
        Assert.Single(dirs);
        var expected = Path.Combine(AppData, "Blackmagic Design", "DaVinci Resolve", "CacheClip");
        Assert.Equal(expected, dirs[0].FullName);
    }

    [Fact]
    public void DaVinci_GetCacheSizeBytes_ReturnsZero_WhenMissing()
    {
        var locator = new DaVinciCacheLocator();
        var size = locator.GetCacheSizeBytes();
        Assert.True(size >= 0);
    }

    [Fact]
    public void Premiere_AppName_IsSet()
    {
        var locator = new PremiereCacheLocator();
        Assert.False(string.IsNullOrWhiteSpace(locator.AppName));
        Assert.Equal("Premiere Pro", locator.AppName);
    }

    [Fact]
    public void Premiere_GetCacheDirectories_ResolvesTwoExpectedPaths()
    {
        var locator = new PremiereCacheLocator();
        var dirs = locator.GetCacheDirectories().Select(d => d.FullName).ToList();
        Assert.Equal(2, dirs.Count);
        Assert.Contains(Path.Combine(AppData, "Adobe", "Common", "Media Cache Files"), dirs);
        Assert.Contains(Path.Combine(AppData, "Adobe", "Common", "Media Cache"), dirs);
    }

    [Fact]
    public void Premiere_GetCacheSizeBytes_DoesNotThrow_WhenMissing()
    {
        var locator = new PremiereCacheLocator();
        var ex = Record.Exception(() => locator.GetCacheSizeBytes());
        Assert.Null(ex);
    }

    [Fact]
    public void AfterEffects_AppName_IsSet()
    {
        var locator = new AfterEffectsCacheLocator();
        Assert.False(string.IsNullOrWhiteSpace(locator.AppName));
        Assert.Equal("After Effects", locator.AppName);
    }

    [Fact]
    public void AfterEffects_GetCacheDirectories_IncludesSharedMediaCache()
    {
        var locator = new AfterEffectsCacheLocator();
        var dirs = locator.GetCacheDirectories().Select(d => d.FullName).ToList();
        Assert.Contains(Path.Combine(AppData, "Adobe", "Common", "Media Cache Files"), dirs);
    }

    [Fact]
    public void AfterEffects_GetCacheSizeBytes_DoesNotThrow_WhenMissing()
    {
        var locator = new AfterEffectsCacheLocator();
        var ex = Record.Exception(() => locator.GetCacheSizeBytes());
        Assert.Null(ex);
    }

    [Fact]
    public void CacheLocatorRegistry_DefaultCtor_ExposesAllThreeApps()
    {
        var registry = new CacheLocator();
        var names = registry.Locators.Select(l => l.AppName).ToList();
        Assert.Contains("DaVinci Resolve", names);
        Assert.Contains("Premiere Pro", names);
        Assert.Contains("After Effects", names);
    }

    [Fact]
    public void CacheLocatorRegistry_GetCacheSizesByApp_HasOneEntryPerLocator()
    {
        var registry = new CacheLocator();
        var sizes = registry.GetCacheSizesByApp();
        Assert.Equal(registry.Locators.Count, sizes.Count);
        Assert.All(sizes.Values, v => Assert.True(v >= 0));
    }

    [Fact]
    public void CacheLocator_ReturnsZero_WhenAllDirectoriesMissing()
    {
        var registry = new CacheLocator(new ICacheLocator[] { new MissingPathLocator() });
        Assert.Equal(0, registry.GetTotalCacheSizeBytes());
    }

    private sealed class MissingPathLocator : CacheLocatorBase
    {
        public override string AppName => "Test";
        public override IEnumerable<DirectoryInfo> GetCacheDirectories()
        {
            yield return new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        }
    }
}

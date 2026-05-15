using SlateClean.Core.CacheLocations;
using Xunit;

namespace SlateClean.Tests;

public class CacheLocationTests
{
    [Fact]
    public void DaVinciLocator_ReturnsAtLeastOneLocation()
    {
        var locator = new DaVinciCacheLocator();
        Assert.NotEmpty(locator.Locate());
    }

    [Fact]
    public void PremiereLocator_ReturnsTwoLocations()
    {
        var locator = new PremiereCacheLocator();
        Assert.Equal(2, locator.Locate().Count());
    }

    [Fact]
    public void AfterEffectsLocator_IncludesSharedMediaCache()
    {
        var locator = new AfterEffectsCacheLocator();
        var locations = locator.Locate().ToList();
        Assert.Contains(locations, l => l.Path.EndsWith("Media Cache Files"));
    }
}

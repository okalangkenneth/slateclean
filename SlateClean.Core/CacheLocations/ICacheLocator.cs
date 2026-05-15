using SlateClean.Core.Models;

namespace SlateClean.Core.CacheLocations;

public interface ICacheLocator
{
    string AppName { get; }
    IEnumerable<CacheLocation> Locate();
}

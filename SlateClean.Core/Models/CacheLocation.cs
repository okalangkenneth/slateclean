namespace SlateClean.Core.Models;

public class CacheLocation
{
    public required string AppName { get; init; }
    public required string Path { get; init; }
    public bool Exists { get; init; }
    public long SizeBytes { get; init; }
}

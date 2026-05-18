namespace SlateClean.App.ViewModels;

public sealed class CacheAppRow
{
    public CacheAppRow(string appName, long sizeBytes)
    {
        AppName = appName;
        SizeBytes = sizeBytes;
        SizeDisplay = ByteFormatter.Format(sizeBytes);
    }

    public string AppName { get; }
    public long SizeBytes { get; }
    public string SizeDisplay { get; }
}

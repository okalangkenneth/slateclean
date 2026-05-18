namespace SlateClean.App.ViewModels;

public sealed class ReviewAppRow
{
    public ReviewAppRow(string appName, int fileCount, long sizeBytes)
    {
        AppName = appName;
        FileCount = fileCount;
        SizeDisplay = ByteFormatter.Format(sizeBytes);
        SummaryDisplay = $"{fileCount} file{(fileCount == 1 ? "" : "s")}";
    }

    public string AppName { get; }
    public int FileCount { get; }
    public string SummaryDisplay { get; }
    public string SizeDisplay { get; }
}

public sealed class ReviewFileRow
{
    public ReviewFileRow(string appName, string path, long sizeBytes)
    {
        AppName = appName;
        Path = path;
        SizeDisplay = ByteFormatter.Format(sizeBytes);
    }

    public string AppName { get; }
    public string Path { get; }
    public string SizeDisplay { get; }
}

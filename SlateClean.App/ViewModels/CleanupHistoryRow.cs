using SlateClean.Core.Models;

namespace SlateClean.App.ViewModels;

public sealed class CleanupHistoryRow
{
    public CleanupHistoryRow(CleanupLog log)
    {
        TimestampDisplay = log.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        AppName = log.AppName;
        FilesDeleted = log.FilesDeleted;
        BytesFreedDisplay = ByteFormatter.Format(log.BytesFreed);
        Success = log.Success;
        StatusDisplay = log.Success ? "OK" : "Failed";
    }

    public string TimestampDisplay { get; }
    public string AppName { get; }
    public int FilesDeleted { get; }
    public string BytesFreedDisplay { get; }
    public bool Success { get; }
    public string StatusDisplay { get; }
}

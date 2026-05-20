namespace SlateClean.Core.Services;

public class DiskSpaceUpdatedEventArgs : EventArgs
{
    public DiskSpaceUpdatedEventArgs(
        string driveRoot, long freeBytes, long totalBytes, DateTime observedAtUtc)
    {
        DriveRoot = driveRoot;
        FreeBytes = freeBytes;
        TotalBytes = totalBytes;
        ObservedAtUtc = observedAtUtc;
    }

    public string DriveRoot { get; }
    public long FreeBytes { get; }
    public long TotalBytes { get; }
    public DateTime ObservedAtUtc { get; }
}

namespace SlateClean.Core.Services;

public class DiskThresholdBreachedEventArgs : EventArgs
{
    public long FreeBytes { get; }
    public long ThresholdBytes { get; }
    public DateTime TimestampUtc { get; }

    public DiskThresholdBreachedEventArgs(long freeBytes, long thresholdBytes, DateTime timestampUtc)
    {
        FreeBytes = freeBytes;
        ThresholdBytes = thresholdBytes;
        TimestampUtc = timestampUtc;
    }
}

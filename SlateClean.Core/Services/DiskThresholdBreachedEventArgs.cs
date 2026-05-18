using SlateClean.Core.Models;

namespace SlateClean.Core.Services;

public class DiskThresholdBreachedEventArgs : EventArgs
{
    public long FreeBytes { get; }
    public long ThresholdBytes { get; }
    public DateTime TimestampUtc { get; }
    public BreachTier Tier { get; }

    public DiskThresholdBreachedEventArgs(
        long freeBytes,
        long thresholdBytes,
        DateTime timestampUtc,
        BreachTier tier)
    {
        FreeBytes = freeBytes;
        ThresholdBytes = thresholdBytes;
        TimestampUtc = timestampUtc;
        Tier = tier;
    }
}

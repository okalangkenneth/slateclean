namespace SlateClean.Core.Models;

public class CleanupLog
{
    public int Id { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public int FilesDeleted { get; set; }
    public long BytesFreed { get; set; }
    public DateTime TimestampUtc { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    // What initiated this cleanup. Values are BreachTier.ToString():
    //   "Manual"              — tray "Clear Now" or any non-plan path
    //   "SoftThreshold"       — user clicked [Clean now] on the Review window
    //   "CriticalThreshold"   — silent auto-execution from 6d
    // Migration on existing DBs defaults legacy rows to "Manual" (historical
    // truth — every prior cleanup was a Clear Now click; the planned paths
    // landed in 6c/6d).
    public string TriggeredBy { get; set; } = nameof(BreachTier.Manual);
}

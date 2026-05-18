namespace SlateClean.Core.Models;

public class AppSettings
{
    public int Id { get; set; }
    public int ThresholdGb { get; set; } = 20;
    public bool AutoCleanEnabled { get; set; } = false;
    public bool RunOnStartup { get; set; } = false;
    public DateTime? LastCleanedUtc { get; set; }

    // Send files to the Recycle Bin instead of permanent delete. Default
    // OFF: media caches are routinely 50–500 GB and Recycle Bin does not
    // reclaim disk space until emptied — recoverability is opt-in.
    public bool SendToRecycleBin { get; set; } = false;

    // Opt-in to silent auto-clean when free space drops below
    // CriticalThresholdGb. Soft-threshold breaches always prompt the user;
    // critical-threshold breaches skip the prompt. The 24-hour modification
    // guard and known-cache-paths rule apply unconditionally in both modes.
    public bool CriticalThresholdEnabled { get; set; } = false;
    public int CriticalThresholdGb { get; set; } = 5;
}

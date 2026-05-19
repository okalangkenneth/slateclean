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

    // Soft-tier breach prompts are suppressed while DateTime.UtcNow is below
    // this value. NULL means "not snoozed". Set by the [Snooze 1h] button on
    // the Review window. Critical-tier breaches IGNORE this — snooze is a
    // UX-interruption defer, not an override of the critical opt-in.
    public DateTime? SnoozedUntilUtc { get; set; }
}

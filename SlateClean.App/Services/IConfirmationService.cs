namespace SlateClean.App.Services;

// Abstraction over user-facing confirmation dialogs. Lets the Settings VM
// gate a dangerous save behind explicit consent without depending on
// MessageBox directly — tests substitute a stub that returns a scripted
// answer.
public interface IConfirmationService
{
    // The user is enabling critical-tier silent auto-cleanup with a threshold
    // ABOVE the drive's current free space, which means a fire on the next
    // poll. Return true to proceed with save, false to cancel and leave the
    // Settings window open with the user's edits intact.
    bool ConfirmCriticalThresholdAboveFreeSpace(int criticalThresholdGb, double currentFreeGb);
}

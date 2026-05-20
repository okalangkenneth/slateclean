namespace SlateClean.App.Services;

public class MessageBoxConfirmationService : IConfirmationService
{
    public bool ConfirmCriticalThresholdAboveFreeSpace(int criticalThresholdGb, double currentFreeGb)
    {
        var message =
            $"Your critical threshold ({criticalThresholdGb} GB) is above your current free space ({currentFreeGb:0.#} GB). " +
            "Saving will trigger silent auto-cleanup on the next disk poll, with no further prompts.\n\n" +
            "Yes — Continue and save.\nNo — Cancel and return to Settings.";

        var result = System.Windows.MessageBox.Show(
            message,
            "Critical threshold above free space",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);

        return result == System.Windows.MessageBoxResult.Yes;
    }
}

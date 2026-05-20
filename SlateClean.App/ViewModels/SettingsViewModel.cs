using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlateClean.App.Services;
using SlateClean.Core.Models;
using SlateClean.Core.Services;

namespace SlateClean.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const long BytesPerGb = 1024L * 1024L * 1024L;

    private readonly SettingsRepository _repo;
    private readonly IConfirmationService _confirmation;
    private readonly Func<long> _getFreeBytes;

    [ObservableProperty] private int _thresholdGb;
    [ObservableProperty] private bool _sendToRecycleBin;
    [ObservableProperty] private bool _criticalThresholdEnabled;
    [ObservableProperty] private int _criticalThresholdGb;

    public string CriticalConsentText =>
        "I understand this will delete cache files without prompting when " +
        "free space drops below the critical threshold. Files modified in " +
        "the last 24 hours remain protected.";

    public SettingsViewModel(
        SettingsRepository repo,
        IConfirmationService confirmation,
        Func<long> getFreeBytes)
    {
        _repo = repo;
        _confirmation = confirmation;
        _getFreeBytes = getFreeBytes;
        Reload();
    }

    // Refreshes VM fields from the persisted row. Called on construction and
    // whenever the window is reopened, so unsaved edits from a previous
    // session are discarded.
    public void Reload()
    {
        var s = _repo.Load();
        ThresholdGb = s.ThresholdGb;
        SendToRecycleBin = s.SendToRecycleBin;
        CriticalThresholdEnabled = s.CriticalThresholdEnabled;
        CriticalThresholdGb = s.CriticalThresholdGb;
    }

    public event EventHandler? RequestClose;

    [RelayCommand]
    private void Save()
    {
        // Guard: critical-tier opt-in with a threshold ABOVE current free space
        // would fire silent auto-cleanup on the very next poll. The repo would
        // happily persist that — so the gate has to live here, before the write.
        // On cancel we leave the VM fields untouched so the user can adjust.
        if (CriticalThresholdEnabled)
        {
            var freeBytes = _getFreeBytes();
            var criticalBytes = (long)CriticalThresholdGb * BytesPerGb;
            if (criticalBytes > freeBytes)
            {
                var freeGb = freeBytes / (double)BytesPerGb;
                if (!_confirmation.ConfirmCriticalThresholdAboveFreeSpace(CriticalThresholdGb, freeGb))
                {
                    return;
                }
            }
        }

        _repo.Save(new AppSettings
        {
            ThresholdGb = ThresholdGb,
            SendToRecycleBin = SendToRecycleBin,
            CriticalThresholdEnabled = CriticalThresholdEnabled,
            CriticalThresholdGb = CriticalThresholdGb,
        });
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        Reload();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}

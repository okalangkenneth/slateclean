using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlateClean.Core.Models;
using SlateClean.Core.Services;

namespace SlateClean.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsRepository _repo;

    [ObservableProperty] private int _thresholdGb;
    [ObservableProperty] private bool _sendToRecycleBin;
    [ObservableProperty] private bool _criticalThresholdEnabled;
    [ObservableProperty] private int _criticalThresholdGb;

    public string CriticalConsentText =>
        "I understand this will delete cache files without prompting when " +
        "free space drops below the critical threshold. Files modified in " +
        "the last 24 hours remain protected.";

    public SettingsViewModel(SettingsRepository repo)
    {
        _repo = repo;
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

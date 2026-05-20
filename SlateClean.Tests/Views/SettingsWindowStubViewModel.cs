using System.Windows.Input;

namespace SlateClean.Tests.Views;

internal sealed class SettingsWindowStubViewModel
{
    public int ThresholdGb { get; set; } = 20;
    public bool SendToRecycleBin { get; set; } = false;
    public bool CriticalThresholdEnabled { get; set; } = false;
    public int CriticalThresholdGb { get; set; } = 5;

    public string CriticalConsentText =>
        "I understand this will delete cache files without prompting when " +
        "free space drops below the critical threshold. Files modified in " +
        "the last 24 hours remain protected.";

    public ICommand SaveCommand { get; } = new NoopCommand();
    public ICommand CancelCommand { get; } = new NoopCommand();

    private sealed class NoopCommand : ICommand
    {
        public event System.EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}

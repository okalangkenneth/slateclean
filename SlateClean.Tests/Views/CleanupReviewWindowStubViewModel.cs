using System.Collections.ObjectModel;
using System.Windows.Input;
using SlateClean.App.ViewModels;

namespace SlateClean.Tests.Views;

internal sealed class CleanupReviewWindowStubViewModel
{
    public string PlanIdDisplay => "abcd1234";
    public string TierDisplay => "Soft threshold breach";
    public string TotalsDisplay => "3 files · 1.2 GB";
    public string StatusDisplay => "Ready";
    public bool IsExecuting => false;
    public string SnoozeTooltip => "Snooze coming in a later update";

    public ObservableCollection<ReviewAppRow> AppRows { get; } = new()
    {
        new ReviewAppRow("DaVinci Resolve", 2, 700_000_000),
        new ReviewAppRow("Premiere Pro", 1, 500_000_000),
    };

    public ObservableCollection<ReviewFileRow> FileRows { get; } = new()
    {
        new ReviewFileRow("DaVinci Resolve", @"C:\Users\Test\AppData\Roaming\Blackmagic Design\DaVinci Resolve\CacheClip\a.dat", 400_000_000),
        new ReviewFileRow("DaVinci Resolve", @"C:\Users\Test\AppData\Roaming\Blackmagic Design\DaVinci Resolve\CacheClip\b.dat", 300_000_000),
        new ReviewFileRow("Premiere Pro", @"C:\Users\Test\AppData\Roaming\Adobe\Common\Media Cache Files\c.dat", 500_000_000),
    };

    public ICommand CleanNowCommand { get; } = new NoopCommand();
    public ICommand CancelCommand { get; } = new NoopCommand();
    public ICommand Snooze1hCommand { get; } = new NoopCommand();

    private sealed class NoopCommand : ICommand
    {
        public event System.EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => false;
        public void Execute(object? parameter) { }
    }
}

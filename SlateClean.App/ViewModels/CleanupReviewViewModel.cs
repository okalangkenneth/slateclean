using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SlateClean.Core.Models;
using SlateClean.Core.Services;

namespace SlateClean.App.ViewModels;

public partial class CleanupReviewViewModel : ObservableObject
{
    private readonly CleanupService _cleanup;
    private readonly ILogger<CleanupReviewViewModel> _logger;
    private CleanupPlan? _plan;

    [ObservableProperty] private string _planIdDisplay = "—";
    [ObservableProperty] private string _tierDisplay = "—";
    [ObservableProperty] private string _totalsDisplay = "—";
    [ObservableProperty] private string _statusDisplay = "Ready";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CleanNowCommand))]
    private bool _isExecuting;

    public ObservableCollection<ReviewAppRow> AppRows { get; } = new();
    public ObservableCollection<ReviewFileRow> FileRows { get; } = new();

    // Snooze 1h is intentionally always-disabled in this slice. Layout is
    // locked in so the button does not move when slice 6e wires it up.
    public string SnoozeTooltip => "Snooze coming in a later update";

    public CleanupReviewViewModel(
        CleanupService cleanup,
        ILogger<CleanupReviewViewModel> logger)
    {
        _cleanup = cleanup;
        _logger = logger;
    }

    public event EventHandler? RequestClose;

    // Replaces any previously-loaded plan. Always call before the window is
    // shown so the user reviews the current breach, not a stale one.
    public void Load(CleanupPlan plan)
    {
        _plan = plan;
        IsExecuting = false;
        PlanIdDisplay = plan.PlanId.ToString().Substring(0, 8);
        TierDisplay = plan.Tier switch
        {
            BreachTier.SoftThreshold => "Soft threshold breach",
            BreachTier.CriticalThreshold => "Critical threshold breach",
            _ => "Manual",
        };
        TotalsDisplay = $"{plan.TotalFiles} file{(plan.TotalFiles == 1 ? "" : "s")} · {ByteFormatter.Format(plan.TotalBytes)}";
        StatusDisplay = "Ready";

        AppRows.Clear();
        FileRows.Clear();
        foreach (var app in plan.Apps)
        {
            AppRows.Add(new ReviewAppRow(app.AppName, app.Files.Count, app.TotalBytes));
            foreach (var file in app.Files)
            {
                FileRows.Add(new ReviewFileRow(app.AppName, file.Path, file.SizeBytes));
            }
        }

        // CleanNow's CanExecute depends on _plan, which is a plain field — no
        // auto-notify. Re-evaluate after the plan is wired up; otherwise the
        // button stays stuck in its initial (disabled) state.
        CleanNowCommand.NotifyCanExecuteChanged();
    }

    private bool CanCleanNow() => !IsExecuting && _plan is not null && _plan.TotalFiles > 0;

    [RelayCommand(CanExecute = nameof(CanCleanNow))]
    private async Task CleanNowAsync()
    {
        var plan = _plan;
        if (plan is null) return;

        IsExecuting = true;
        StatusDisplay = "Cleaning…";
        try
        {
            await _cleanup.ExecutePlanAsync(plan);
            StatusDisplay = "Done";
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Review] Plan {PlanId} execution failed", plan.PlanId);
            StatusDisplay = "Failed — see log";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, EventArgs.Empty);

    // Snooze is a placeholder for 6e. CanExecute is always false here so the
    // command never runs, but the button stays visible in the layout.
    [RelayCommand(CanExecute = nameof(CanSnooze))]
    private void Snooze1h() { /* enabled in slice 6e */ }

    private bool CanSnooze() => false;
}

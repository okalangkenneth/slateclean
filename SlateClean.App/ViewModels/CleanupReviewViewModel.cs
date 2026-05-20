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
    private readonly SettingsRepository _settings;
    private readonly ILogger<CleanupReviewViewModel> _logger;
    private readonly Func<DateTime> _utcNow;
    private CleanupPlan? _plan;

    [ObservableProperty] private string _planIdDisplay = "—";
    [ObservableProperty] private string _tierDisplay = "—";
    [ObservableProperty] private string _totalsDisplay = "—";
    [ObservableProperty] private string _statusDisplay = "Ready";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CleanNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(Snooze1hCommand))]
    private bool _isExecuting;

    public ObservableCollection<ReviewAppRow> AppRows { get; } = new();
    public ObservableCollection<ReviewFileRow> FileRows { get; } = new();

    public string SnoozeTooltip => "Defer this prompt for 1 hour. Critical-threshold breaches still fire silently.";

    public CleanupReviewViewModel(
        CleanupService cleanup,
        SettingsRepository settings,
        ILogger<CleanupReviewViewModel> logger,
        Func<DateTime>? utcNow = null)
    {
        _cleanup = cleanup;
        _settings = settings;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
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

        // CleanNow + Snooze1h CanExecute depends on _plan (a plain field) —
        // no auto-notify. Re-evaluate after the plan is wired up; otherwise
        // the buttons stay stuck in their initial (disabled) state.
        CleanNowCommand.NotifyCanExecuteChanged();
        Snooze1hCommand.NotifyCanExecuteChanged();
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

    // Defers the soft-tier prompt for one hour by persisting SnoozedUntilUtc
    // on AppSettings. DiskMonitorService consults that field on every poll
    // (soft tier only — critical breaches ignore snooze and still fire
    // silently). The 1h window expires naturally on the next poll where
    // DateTime.UtcNow >= SnoozedUntilUtc; no background timer needed.
    [RelayCommand(CanExecute = nameof(CanSnooze))]
    private void Snooze1h()
    {
        var until = _utcNow().AddHours(1);
        var settings = _settings.Load();
        settings.SnoozedUntilUtc = until;
        _settings.Save(settings);
        _logger.LogInformation("[Snooze] User snoozed cleanup until {SnoozedUntilUtc:o}", until);
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    // Snooze is enabled under the same conditions as Clean now — both are
    // valid responses to a plan with eligible files (one executes, one
    // defers). Cancel remains always-enabled separately.
    private bool CanSnooze() => !IsExecuting && _plan is not null && _plan.TotalFiles > 0;
}

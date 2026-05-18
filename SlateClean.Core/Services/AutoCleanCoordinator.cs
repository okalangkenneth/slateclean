using Microsoft.Extensions.Logging;
using SlateClean.Core.Models;

namespace SlateClean.Core.Services;

// Subscribes to threshold-breach events and turns them into CleanupPlans.
// At this slice (6b) the coordinator only *builds* a plan and logs a summary
// — no deletion, no UI prompt, no execution. The full pipeline runs end-to-end
// in a safe dry-run mode so we can verify behaviour before wiring execution
// in the next slice (6c).
public class AutoCleanCoordinator : IDisposable
{
    private readonly DiskMonitorService _monitor;
    private readonly CleanupService _cleanup;
    private readonly ILogger<AutoCleanCoordinator> _logger;

    // Surfaced for the future Review UI and for tests. Raised after a plan is
    // built in response to a soft- or critical-threshold breach.
    public event EventHandler<CleanupPlan>? PlanBuilt;

    public AutoCleanCoordinator(
        DiskMonitorService monitor,
        CleanupService cleanup,
        ILogger<AutoCleanCoordinator> logger)
    {
        _monitor = monitor;
        _cleanup = cleanup;
        _logger = logger;
        _monitor.DiskThresholdBreached += OnDiskThresholdBreached;
    }

    private async void OnDiskThresholdBreached(object? sender, DiskThresholdBreachedEventArgs e)
    {
        try
        {
            var plan = await _cleanup.BuildPlanAsync(e.Tier);
            _logger.LogWarning(
                "[AutoClean] {Tier} breach detected — plan {PlanId}: {Files} files / {Bytes} bytes across {Apps} app(s); no action taken (dry-run)",
                e.Tier, plan.PlanId, plan.TotalFiles, plan.TotalBytes, plan.Apps.Count);
            PlanBuilt?.Invoke(this, plan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoClean] Failed to build plan for {Tier} breach", e.Tier);
        }
    }

    public void Dispose()
    {
        _monitor.DiskThresholdBreached -= OnDiskThresholdBreached;
    }
}

using Microsoft.Extensions.Logging;
using SlateClean.Core.Models;

namespace SlateClean.Core.Services;

// Routes a breach event to the correct path:
//
//   SoftThreshold → BuildPlan + raise PlanBuilt (the tray picks this up to
//                   show the "Cleanup recommended" balloon → Review window
//                   → user clicks Clean now → ExecutePlanAsync).
//
//   CriticalThreshold + opt-in → BuildPlan + ExecutePlanAsync silently
//                                (no balloon, no Review window) + write a
//                                CriticalFire audit row. Raises PlanExecuted
//                                so subscribers (and tests) can react after
//                                the silent run completes.
//
//   CriticalThreshold + opt-in DISABLED → no-op. DiskMonitorService already
//                                         gates critical events on the opt-in,
//                                         but the coordinator re-checks: the
//                                         opt-in is the gate, not a hint.
//
// Safety invariants enforced by reuse, not by promise:
//   - silent execution calls the SAME CleanupService.ExecutePlanAsync that
//     the Review window's [Clean now] button calls
//   - 24h modification guard / known-paths-only live in EnumerateEligibleFiles
//   - hysteresis + 5-min throttle live in DiskMonitorService
//   - the plan built at breach time is the exact plan handed to the executor
public class AutoCleanCoordinator : IDisposable
{
    private readonly DiskMonitorService _monitor;
    private readonly CleanupService _cleanup;
    private readonly SettingsRepository _settings;
    private readonly ILogger<AutoCleanCoordinator> _logger;
    private readonly Func<DateTime> _utcNow;

    public event EventHandler<CleanupPlan>? PlanBuilt;

    // Raised after a silent critical-tier execution completes (success or
    // failure). The argument is the plan that was executed — subscribers
    // can match it against the CleanupLog rows just written. Tests use this
    // to assert the silent path ran without polling.
    public event EventHandler<CleanupPlan>? PlanExecuted;

    public AutoCleanCoordinator(
        DiskMonitorService monitor,
        CleanupService cleanup,
        SettingsRepository settings,
        ILogger<AutoCleanCoordinator> logger,
        Func<DateTime>? utcNow = null)
    {
        _monitor = monitor;
        _cleanup = cleanup;
        _settings = settings;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _monitor.DiskThresholdBreached += OnDiskThresholdBreached;
    }

    private async void OnDiskThresholdBreached(object? sender, DiskThresholdBreachedEventArgs e)
    {
        try { await HandleBreachAsync(e); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoClean] Unhandled error in breach handler for {Tier}", e.Tier);
        }
    }

    // Internal so tests can drive the branching directly without having to
    // round-trip through DiskMonitorService. That matters specifically for
    // the opt-in-disabled defense test: DiskMonitorService refuses to fire
    // critical events when the opt-in is false, so the only way to verify
    // the coordinator's own defense check is to invoke this directly.
    internal async Task HandleBreachAsync(DiskThresholdBreachedEventArgs e)
    {
        if (e.Tier == BreachTier.CriticalThreshold)
        {
            await HandleCriticalAsync(e);
            return;
        }

        // Soft tier (and Manual, if it ever flows here): build a plan and
        // let subscribers decide what to do with it.
        var plan = await _cleanup.BuildPlanAsync(e.Tier);
        _logger.LogWarning(
            "[AutoClean] {Tier} breach detected — plan {PlanId}: {Files} files / {Bytes} bytes across {Apps} app(s)",
            e.Tier, plan.PlanId, plan.TotalFiles, plan.TotalBytes, plan.Apps.Count);
        PlanBuilt?.Invoke(this, plan);
    }

    private async Task HandleCriticalAsync(DiskThresholdBreachedEventArgs e)
    {
        // Defense-in-depth: re-check the opt-in inside the coordinator even
        // though DiskMonitorService already gates critical events on it. If
        // the user has not consented, do not delete anything regardless of
        // how the event arrived.
        var settings = _settings.Load();
        if (!settings.CriticalThresholdEnabled)
        {
            _logger.LogWarning(
                "[AutoClean] Critical breach received but opt-in is disabled — refusing to execute");
            return;
        }

        var plan = await _cleanup.BuildPlanAsync(BreachTier.CriticalThreshold);
        _logger.LogWarning(
            "[AutoClean] CriticalThreshold breach — silent execution starting; plan {PlanId}: {Files} files / {Bytes} bytes",
            plan.PlanId, plan.TotalFiles, plan.TotalBytes);

        // Note: deliberately not raising PlanBuilt for critical — the tray
        // listens on PlanBuilt for the balloon/review prompt and that path
        // must stay silent here.

        IReadOnlyList<CleanupLog> logs;
        try
        {
            logs = await _cleanup.ExecutePlanAsync(plan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoClean] Critical plan {PlanId} execution failed", plan.PlanId);
            PlanExecuted?.Invoke(this, plan);
            return;
        }

        var filesDeleted = logs.Sum(l => l.FilesDeleted);
        var bytesFreed = logs.Sum(l => l.BytesFreed);

        try
        {
            _settings.WriteCriticalFireAudit(
                timestampUtc: _utcNow(),
                freeBytesAtTrigger: e.FreeBytes,
                criticalThresholdGb: settings.CriticalThresholdGb,
                planId: plan.PlanId,
                filesDeleted: filesDeleted,
                bytesFreed: bytesFreed);
        }
        catch (Exception ex)
        {
            // Audit-log write failure must not crash the app, but it IS the
            // accountability surface — log loudly so manual verification
            // notices.
            _logger.LogError(ex,
                "[AutoClean] Failed to write CriticalFire audit row for plan {PlanId}",
                plan.PlanId);
        }

        _logger.LogWarning(
            "[AutoClean] CriticalThreshold silent execution complete — plan {PlanId}: {Files} files, {Bytes} bytes freed",
            plan.PlanId, filesDeleted, bytesFreed);

        PlanExecuted?.Invoke(this, plan);
    }

    public void Dispose()
    {
        _monitor.DiskThresholdBreached -= OnDiskThresholdBreached;
    }
}

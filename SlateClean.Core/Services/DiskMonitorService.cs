using Microsoft.Extensions.Logging;
using SlateClean.Core.CacheLocations;
using SlateClean.Core.Models;

namespace SlateClean.Core.Services;

public class DiskMonitorService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EventThrottle = TimeSpan.FromMinutes(5);

    private readonly ICacheLocator[] _locators;
    private readonly Func<AppSettings> _getSettings;
    private readonly ILogger<DiskMonitorService> _logger;
    private readonly Func<DateTime> _utcNow;
    private Timer? _timer;
    private DateTime? _lastEventAtUtc;
    private readonly object _gate = new();

    // Per-tier hysteresis state: lives here (not in subscribers) so every
    // consumer of DiskThresholdBreached sees the same edge-triggered semantics.
    // Set to true the moment we observe free space below the tier's threshold;
    // cleared the moment we observe free space at or above it. An event fires
    // only on the false→true transition (combined with the 5-min throttle below).
    private bool _softBelow;
    private bool _criticalBelow;

    public DiskMonitorService(
        ICacheLocator[] locators,
        Func<AppSettings> getSettings,
        ILogger<DiskMonitorService> logger,
        Func<DateTime>? utcNow = null)
    {
        _locators = locators;
        _getSettings = getSettings;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string DriveRoot { get; set; } = "C:\\";

    public event EventHandler<DiskThresholdBreachedEventArgs>? DiskThresholdBreached;
    public event EventHandler<DiskSpaceUpdatedEventArgs>? DiskSpaceUpdated;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_timer is not null) return Task.CompletedTask;
            _timer = new Timer(_ => SafePoll(), null, TimeSpan.Zero, PollInterval);
            _logger.LogInformation("[DiskMonitor] Started polling {Drive} every {Interval}s",
                DriveRoot, (int)PollInterval.TotalSeconds);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _logger.LogInformation("[DiskMonitor] Stopped");
        }
        return Task.CompletedTask;
    }

    public void Poll()
    {
        long freeBytes;
        long totalBytes;
        try
        {
            freeBytes = GetFreeBytes(DriveRoot);
            totalBytes = GetTotalBytes(DriveRoot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DiskMonitor] Failed to read free space on {Drive}", DriveRoot);
            return;
        }

        var observedAt = _utcNow();
        DiskSpaceUpdated?.Invoke(this,
            new DiskSpaceUpdatedEventArgs(DriveRoot, freeBytes, totalBytes, observedAt));

        var settings = _getSettings();
        var softBytes = (long)settings.ThresholdGb * 1024L * 1024L * 1024L;
        var criticalBytes = (long)settings.CriticalThresholdGb * 1024L * 1024L * 1024L;

        // Capture previous state, then update from current observation.
        // Critical is only considered "below" if the user opted into the tier.
        var softWasBelow = _softBelow;
        var criticalWasBelow = _criticalBelow;
        _softBelow = freeBytes < softBytes;
        _criticalBelow = settings.CriticalThresholdEnabled && freeBytes < criticalBytes;

        // Edge-trigger: only the false→true transition fires. Critical takes
        // precedence when both tiers transition in the same poll (e.g. a sudden
        // drop), since silent execution is the more urgent path.
        BreachTier? tier = null;
        long thresholdBytes = 0;
        if (_criticalBelow && !criticalWasBelow)
        {
            tier = BreachTier.CriticalThreshold;
            thresholdBytes = criticalBytes;
        }
        else if (_softBelow && !softWasBelow)
        {
            tier = BreachTier.SoftThreshold;
            thresholdBytes = softBytes;
        }

        if (tier is null) return;

        // Snooze filter — soft tier only. The user's [Snooze 1h] click is a
        // UX-interruption defer for the prompt path; critical breaches are
        // the dangerous path and must NOT be subject to it. Filter order is
        // snooze → hysteresis → throttle → emit, with snooze outermost
        // because it represents an explicit user signal.
        if (tier == BreachTier.SoftThreshold
            && settings.SnoozedUntilUtc is { } snoozedUntil
            && snoozedUntil > observedAt)
        {
            _logger.LogInformation(
                "[DiskMonitor] Soft breach suppressed — snoozed until {SnoozedUntilUtc:o}",
                snoozedUntil);
            return;
        }

        // Shared 5-min throttle across tiers. Pairs with hysteresis: even rapid
        // above→below oscillations cannot fire more than once per 5 minutes.
        lock (_gate)
        {
            if (_lastEventAtUtc is not null && observedAt - _lastEventAtUtc.Value < EventThrottle)
            {
                _logger.LogDebug("[DiskMonitor] {Tier} breach suppressed (throttled)", tier);
                return;
            }
            _lastEventAtUtc = observedAt;
        }

        _logger.LogWarning(
            "[DiskMonitor] {Tier} breach: free {FreeGb}GB below {ThresholdGb}GB",
            tier, freeBytes / 1024L / 1024L / 1024L, thresholdBytes / 1024L / 1024L / 1024L);

        DiskThresholdBreached?.Invoke(this,
            new DiskThresholdBreachedEventArgs(freeBytes, thresholdBytes, observedAt, tier.Value));
    }

    protected virtual long GetFreeBytes(string driveRoot) =>
        new DriveInfo(driveRoot).AvailableFreeSpace;

    protected virtual long GetTotalBytes(string driveRoot) =>
        new DriveInfo(driveRoot).TotalSize;

    private void SafePoll()
    {
        try { Poll(); }
        catch (Exception ex) { _logger.LogError(ex, "[DiskMonitor] Poll failed"); }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}

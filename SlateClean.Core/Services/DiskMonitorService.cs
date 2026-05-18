using Microsoft.Extensions.Logging;
using SlateClean.Core.CacheLocations;

namespace SlateClean.Core.Services;

public class DiskMonitorService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EventThrottle = TimeSpan.FromMinutes(5);

    private readonly ICacheLocator[] _locators;
    private readonly ILogger<DiskMonitorService> _logger;
    private readonly Func<DateTime> _utcNow;
    private Timer? _timer;
    private DateTime? _lastEventAtUtc;
    private readonly object _gate = new();

    public DiskMonitorService(
        ICacheLocator[] locators,
        ILogger<DiskMonitorService> logger,
        Func<DateTime>? utcNow = null)
    {
        _locators = locators;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public int ThresholdGb { get; set; } = 20;
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
        var thresholdBytes = (long)ThresholdGb * 1024L * 1024L * 1024L;
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

        if (freeBytes >= thresholdBytes) return;

        var now = observedAt;
        lock (_gate)
        {
            if (_lastEventAtUtc is not null && now - _lastEventAtUtc.Value < EventThrottle)
            {
                _logger.LogDebug("[DiskMonitor] Threshold breached but suppressed (throttled)");
                return;
            }
            _lastEventAtUtc = now;
        }

        _logger.LogWarning("[DiskMonitor] Free space {FreeGb}GB below threshold {ThresholdGb}GB",
            freeBytes / 1024L / 1024L / 1024L, ThresholdGb);
        DiskThresholdBreached?.Invoke(this,
            new DiskThresholdBreachedEventArgs(freeBytes, thresholdBytes, now));
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

using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlateClean.Core.Services;

namespace SlateClean.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly Dispatcher _dispatcher;
    private readonly CacheLocator _cacheLocator;
    private readonly CleanupHistoryService _history;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DriveLabel))]
    private string _driveRoot = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsedBytes))]
    [NotifyPropertyChangedFor(nameof(UsedPercent))]
    [NotifyPropertyChangedFor(nameof(FreeDisplay))]
    private long _freeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsedBytes))]
    [NotifyPropertyChangedFor(nameof(UsedPercent))]
    [NotifyPropertyChangedFor(nameof(FreeDisplay))]
    [NotifyPropertyChangedFor(nameof(TotalDisplay))]
    private long _totalBytes;

    [ObservableProperty]
    private bool _hasReading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastUpdatedDisplay))]
    private DateTime _lastUpdatedUtc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalCacheDisplay))]
    private long _totalCacheBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CachesScannedAtDisplay))]
    private DateTime? _cachesScannedAtUtc;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCacheSizesCommand))]
    private bool _isRefreshingCaches;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshHistoryCommand))]
    private bool _isRefreshingHistory;

    public ObservableCollection<CacheAppRow> CacheApps { get; } = new();
    public ObservableCollection<CleanupHistoryRow> RecentCleanups { get; } = new();

    public DashboardViewModel(
        DiskMonitorService diskMonitor,
        CacheLocator cacheLocator,
        CleanupHistoryService history,
        CleanupService cleanupService)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _cacheLocator = cacheLocator;
        _history = history;
        diskMonitor.DiskSpaceUpdated += OnDiskSpaceUpdated;
        cleanupService.CleanupCompleted += OnCleanupCompleted;

        // Fire-and-forget initial scans. The Task.Run inside the cache command
        // keeps enumeration off the UI thread; the await continuation marshals
        // back via the captured SynchronizationContext.
        _ = RefreshCacheSizesCommand.ExecuteAsync(null);
        _ = RefreshHistoryCommand.ExecuteAsync(null);
    }

    public string DriveLabel => DriveRoot.TrimEnd('\\');
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    public double UsedPercent =>
        TotalBytes <= 0 ? 0d : Math.Clamp((UsedBytes / (double)TotalBytes) * 100d, 0d, 100d);

    public string FreeDisplay => HasReading
        ? $"{ByteFormatter.Format(FreeBytes)} free of {ByteFormatter.Format(TotalBytes)}"
        : "Waiting for first reading…";

    public string TotalDisplay => HasReading ? ByteFormatter.Format(TotalBytes) : "—";

    public string LastUpdatedDisplay =>
        HasReading ? LastUpdatedUtc.ToLocalTime().ToString("HH:mm:ss") : "—";

    public string TotalCacheDisplay => ByteFormatter.Format(TotalCacheBytes);

    public string CachesScannedAtDisplay =>
        CachesScannedAtUtc.HasValue
            ? CachesScannedAtUtc.Value.ToLocalTime().ToString("HH:mm:ss")
            : "—";

    private bool CanRefreshCaches() => !IsRefreshingCaches;

    [RelayCommand(CanExecute = nameof(CanRefreshCaches))]
    private async Task RefreshCacheSizesAsync()
    {
        IsRefreshingCaches = true;
        try
        {
            // Recursive directory enumeration on potentially-huge media caches
            // must not block the UI thread.
            var sizes = await Task.Run(() => _cacheLocator.GetCacheSizesByApp());

            CacheApps.Clear();
            long total = 0;
            foreach (var kvp in sizes)
            {
                CacheApps.Add(new CacheAppRow(kvp.Key, kvp.Value));
                total += kvp.Value;
            }
            TotalCacheBytes = total;
            CachesScannedAtUtc = DateTime.UtcNow;
        }
        finally
        {
            IsRefreshingCaches = false;
        }
    }

    private bool CanRefreshHistory() => !IsRefreshingHistory;

    [RelayCommand(CanExecute = nameof(CanRefreshHistory))]
    private async Task RefreshHistoryAsync()
    {
        IsRefreshingHistory = true;
        try
        {
            var rows = await _history.GetRecentAsync(50);

            RecentCleanups.Clear();
            foreach (var log in rows)
            {
                RecentCleanups.Add(new CleanupHistoryRow(log));
            }
        }
        finally
        {
            IsRefreshingHistory = false;
        }
    }

    private void OnCleanupCompleted(object? sender, EventArgs e)
    {
        // CleanupService raises this from a worker context; marshal to the UI
        // thread before kicking off the refresh so the ObservableCollection
        // mutation happens where WPF expects it.
        _dispatcher.BeginInvoke(() => _ = RefreshHistoryCommand.ExecuteAsync(null));
    }

    private void OnDiskSpaceUpdated(object? sender, DiskSpaceUpdatedEventArgs e)
    {
        // Timer callback is on a ThreadPool thread; marshal to UI thread so
        // PropertyChanged subscribers (WPF bindings) update safely.
        _dispatcher.BeginInvoke(() =>
        {
            DriveRoot = e.DriveRoot;
            FreeBytes = e.FreeBytes;
            TotalBytes = e.TotalBytes;
            LastUpdatedUtc = e.ObservedAtUtc;
            HasReading = true;
            OnPropertyChanged(nameof(FreeDisplay));
            OnPropertyChanged(nameof(TotalDisplay));
            OnPropertyChanged(nameof(LastUpdatedDisplay));
        });
    }
}

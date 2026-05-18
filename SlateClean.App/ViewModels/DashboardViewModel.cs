using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SlateClean.Core.Services;

namespace SlateClean.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private const double BytesPerGb = 1024d * 1024d * 1024d;
    private const double BytesPerTb = BytesPerGb * 1024d;

    private readonly Dispatcher _dispatcher;

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

    public DashboardViewModel(DiskMonitorService diskMonitor)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        diskMonitor.DiskSpaceUpdated += OnDiskSpaceUpdated;
    }

    public string DriveLabel => DriveRoot.TrimEnd('\\');
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    public double UsedPercent =>
        TotalBytes <= 0 ? 0d : Math.Clamp((UsedBytes / (double)TotalBytes) * 100d, 0d, 100d);

    public string FreeDisplay =>
        HasReading ? $"{Format(FreeBytes)} free of {Format(TotalBytes)}" : "Waiting for first reading…";

    public string TotalDisplay => HasReading ? Format(TotalBytes) : "—";

    public string LastUpdatedDisplay =>
        HasReading ? LastUpdatedUtc.ToLocalTime().ToString("HH:mm:ss") : "—";

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

    private static string Format(long bytes)
    {
        if (bytes <= 0) return "0 GB";
        double tb = bytes / BytesPerTb;
        if (tb >= 1.0) return $"{tb:F2} TB";
        double gb = bytes / BytesPerGb;
        return $"{gb:F1} GB";
    }
}

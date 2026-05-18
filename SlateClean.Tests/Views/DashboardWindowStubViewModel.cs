using System.Collections.ObjectModel;
using System.Windows.Input;
using SlateClean.App.ViewModels;

namespace SlateClean.Tests.Views;

// Mirrors the public binding surface of DashboardViewModel without touching
// DiskMonitorService or CacheLocator. Lets the smoke test exercise every
// {Binding ...} in DashboardWindow.xaml.
internal sealed class DashboardWindowStubViewModel
{
    public string DriveLabel => "C:";
    public long FreeBytes => 50L * 1024 * 1024 * 1024;
    public long TotalBytes => 500L * 1024 * 1024 * 1024;
    public long UsedBytes => TotalBytes - FreeBytes;
    public double UsedPercent => 90.0;
    public bool HasReading => true;
    public string FreeDisplay => "50 GB free of 500 GB";
    public string TotalDisplay => "500 GB";
    public string LastUpdatedDisplay => "12:00:00";
    public long TotalCacheBytes => 0;
    public string TotalCacheDisplay => "0 B";
    public string CachesScannedAtDisplay => "12:00:00";
    public bool IsRefreshingCaches => false;

    public ObservableCollection<CacheAppRow> CacheApps { get; } = new()
    {
        new CacheAppRow("DaVinci Resolve", 0),
        new CacheAppRow("Premiere Pro", 0),
        new CacheAppRow("After Effects", 0),
    };

    public ICommand RefreshCacheSizesCommand { get; } = new NoopCommand();

    private sealed class NoopCommand : ICommand
    {
        public event System.EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}

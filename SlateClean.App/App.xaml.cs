using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SlateClean.App.TrayIcon;
using SlateClean.Core.CacheLocations;
using SlateClean.Core.Data;
using SlateClean.Core.Services;

namespace SlateClean.App;

public partial class App : System.Windows.Application
{
    private SlateCleanDbContext? _db;
    private DiskMonitorService? _diskMonitor;
    private TrayIconManager? _tray;
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _db = new SlateCleanDbContext();
        _db.Database.EnsureCreated();

        var locators = new ICacheLocator[]
        {
            new DaVinciCacheLocator(),
            new PremiereCacheLocator(),
            new AfterEffectsCacheLocator(),
        };

        var cacheLocator = new CacheLocator(locators);
        var cleanupService = new CleanupService(
            locators, _db, _loggerFactory.CreateLogger<CleanupService>());
        _diskMonitor = new DiskMonitorService(
            locators, _loggerFactory.CreateLogger<DiskMonitorService>());
        var startupService = new StartupService(
            _loggerFactory.CreateLogger<StartupService>());

        _tray = new TrayIconManager(
            cacheLocator, cleanupService, _diskMonitor, startupService,
            _loggerFactory.CreateLogger<TrayIconManager>());
        _tray.Show();

        await _diskMonitor.StartAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_diskMonitor is not null) await _diskMonitor.StopAsync();
        _diskMonitor?.Dispose();
        _tray?.Dispose();
        _db?.Dispose();
        base.OnExit(e);
    }
}

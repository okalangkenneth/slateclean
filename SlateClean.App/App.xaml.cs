using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SlateClean.App.TrayIcon;
using SlateClean.App.ViewModels;
using SlateClean.App.Views;
using SlateClean.Core.CacheLocations;
using SlateClean.Core.Data;
using SlateClean.Core.Services;

namespace SlateClean.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private DiskMonitorService? _diskMonitor;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        // Eagerly create the SQLite database and apply any pending schema
        // additions before any service uses it. EnsureSchemaUpToDate is
        // idempotent: on fresh installs it just creates tables; on upgraded
        // installs it adds new columns/tables without dropping user data.
        var db = _host.Services.GetRequiredService<SlateCleanDbContext>();
        db.EnsureSchemaUpToDate();

        // Resolve the tray manager (which also resolves the singleton dashboard).
        var tray = _host.Services.GetRequiredService<TrayIconManager>();
        tray.Show();

        // Resolve the coordinator before starting the monitor so its subscription
        // is in place by the time the first Poll fires (which happens on Start
        // since the timer's due-time is TimeSpan.Zero).
        _host.Services.GetRequiredService<AutoCleanCoordinator>();

        _diskMonitor = _host.Services.GetRequiredService<DiskMonitorService>();
        await _diskMonitor.StartAsync();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder => builder.AddDebug());

        // Core — SQLite context as singleton so all services share state.
        services.AddSingleton<SlateCleanDbContext>(_ => new SlateCleanDbContext());

        // Cache locators — registered against ICacheLocator so that
        // IEnumerable<ICacheLocator> resolves to all three in the aggregator.
        // The ICacheLocator[] registration is for CleanupService, which takes
        // an array; it forwards to the same singletons via IEnumerable resolution.
        services.AddSingleton<ICacheLocator, DaVinciCacheLocator>();
        services.AddSingleton<ICacheLocator, PremiereCacheLocator>();
        services.AddSingleton<ICacheLocator, AfterEffectsCacheLocator>();
        services.AddSingleton<ICacheLocator[]>(sp =>
            sp.GetServices<ICacheLocator>().ToArray());

        services.AddSingleton<CacheLocator>();
        services.AddSingleton<CleanupService>();
        services.AddSingleton<CleanupHistoryService>();
        services.AddSingleton<DiskMonitorService>(sp => new DiskMonitorService(
            sp.GetRequiredService<ICacheLocator[]>(),
            () => sp.GetRequiredService<SettingsRepository>().Load(),
            sp.GetRequiredService<ILogger<DiskMonitorService>>()));
        services.AddSingleton<StartupService>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<AutoCleanCoordinator>();

        // ViewModels and Windows — singletons so window state and VM state
        // persist across hide/show cycles.
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<DashboardWindow>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SettingsWindow>();

        // Tray must be singleton — only one icon in the notification area.
        services.AddSingleton<TrayIconManager>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_diskMonitor is not null) await _diskMonitor.StopAsync();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}

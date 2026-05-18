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

        // Eagerly create the SQLite database before any service uses it.
        var db = _host.Services.GetRequiredService<SlateCleanDbContext>();
        db.Database.EnsureCreated();

        // Resolve the tray manager (which also resolves the singleton dashboard).
        var tray = _host.Services.GetRequiredService<TrayIconManager>();
        tray.Show();

        _diskMonitor = _host.Services.GetRequiredService<DiskMonitorService>();
        await _diskMonitor.StartAsync();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder => builder.AddDebug());

        // Core — SQLite context as singleton so all services share state.
        services.AddSingleton<SlateCleanDbContext>(_ => new SlateCleanDbContext());

        // Cache locators — registered individually and as an array for
        // services that take ICacheLocator[].
        services.AddSingleton<DaVinciCacheLocator>();
        services.AddSingleton<PremiereCacheLocator>();
        services.AddSingleton<AfterEffectsCacheLocator>();
        services.AddSingleton<ICacheLocator[]>(sp => new ICacheLocator[]
        {
            sp.GetRequiredService<DaVinciCacheLocator>(),
            sp.GetRequiredService<PremiereCacheLocator>(),
            sp.GetRequiredService<AfterEffectsCacheLocator>(),
        });

        services.AddSingleton<CacheLocator>();
        services.AddSingleton<CleanupService>();
        services.AddSingleton<DiskMonitorService>();
        services.AddSingleton<StartupService>();

        // ViewModels and Windows — singletons so window state and VM state
        // persist across hide/show cycles.
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<DashboardWindow>();

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

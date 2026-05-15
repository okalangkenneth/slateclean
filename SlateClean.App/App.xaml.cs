using System.Windows;
using SlateClean.App.TrayIcon;

namespace SlateClean.App;

public partial class App : System.Windows.Application
{
    private TrayIconManager? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _tray = new TrayIconManager();
        _tray.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}

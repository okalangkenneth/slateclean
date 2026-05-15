using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace SlateClean.App.TrayIcon;

public class TrayIconManager : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;

    public TrayIconManager()
    {
        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = false,
            Text = "SlateClean",
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open Dashboard", null, OnOpenDashboard);
        menu.Items.Add("Clear Now", null, OnClearNow);
        menu.Items.Add("Settings", null, OnSettings);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, OnExit);

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += OnOpenDashboard;
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
    }

    private void OnOpenDashboard(object? sender, EventArgs e)
    {
        System.Windows.MessageBox.Show("Dashboard placeholder.", "SlateClean");
    }

    private void OnClearNow(object? sender, EventArgs e)
    {
        System.Windows.MessageBox.Show("Clear Now placeholder.", "SlateClean");
    }

    private void OnSettings(object? sender, EventArgs e)
    {
        System.Windows.MessageBox.Show("Settings placeholder.", "SlateClean");
    }

    private void OnExit(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}

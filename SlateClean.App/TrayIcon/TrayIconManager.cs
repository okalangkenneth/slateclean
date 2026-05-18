using System.Diagnostics;
using System.Drawing;
using System.Windows;
using Microsoft.Extensions.Logging;
using SlateClean.Core.Services;
using SlateClean.App.Views;
using WinForms = System.Windows.Forms;

namespace SlateClean.App.TrayIcon;

public class TrayIconManager : IDisposable
{
    private readonly CacheLocator _cacheLocator;
    private readonly CleanupService _cleanupService;
    private readonly DiskMonitorService _diskMonitor;
    private readonly StartupService _startupService;
    private readonly DashboardWindow _dashboard;
    private readonly SettingsWindow _settings;
    private readonly ILogger<TrayIconManager> _logger;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _startupItem;

    public TrayIconManager(
        CacheLocator cacheLocator,
        CleanupService cleanupService,
        DiskMonitorService diskMonitor,
        StartupService startupService,
        DashboardWindow dashboard,
        SettingsWindow settings,
        ILogger<TrayIconManager> logger)
    {
        _cacheLocator = cacheLocator;
        _cleanupService = cleanupService;
        _diskMonitor = diskMonitor;
        _startupService = startupService;
        _dashboard = dashboard;
        _settings = settings;
        _logger = logger;

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = false,
            Text = "SlateClean",
        };

        var menu = new WinForms.ContextMenuStrip();

        var openItem = new WinForms.ToolStripMenuItem("Open Dashboard");
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        openItem.Click += OnOpenDashboard;
        menu.Items.Add(openItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Clear Now", null, OnClearNow);

        _startupItem = new WinForms.ToolStripMenuItem("Run at Windows startup")
        {
            CheckOnClick = false,
            Checked = _startupService.IsRunOnStartupEnabled(),
        };
        _startupItem.Click += OnToggleStartup;
        menu.Items.Add(_startupItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Settings…", null, OnOpenSettings);
        menu.Items.Add("Exit", null, OnExit);

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += OnOpenDashboard;

        _diskMonitor.DiskThresholdBreached += OnThresholdBreached;
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
    }

    private void OnOpenDashboard(object? sender, EventArgs e)
    {
        if (!_dashboard.IsVisible)
        {
            _dashboard.Show();
        }

        if (_dashboard.WindowState == WindowState.Minimized)
        {
            _dashboard.WindowState = WindowState.Normal;
        }

        _dashboard.Activate();
        _dashboard.Topmost = true;
        _dashboard.Topmost = false;
        _dashboard.Focus();
    }

    private async void OnClearNow(object? sender, EventArgs e)
    {
        _logger.LogInformation("[Tray] Clear Now invoked");
        try
        {
            var results = (await _cleanupService.CleanAllAsync()).ToList();
            long totalBytes = results.Sum(r => r.BytesFreed);
            int appCount = results.Count(r => r.Success && r.FilesDeleted > 0);
            var mb = totalBytes / (1024.0 * 1024.0);
            _notifyIcon.ShowBalloonTip(
                5000,
                "SlateClean",
                $"Cleaned {mb:F1} MB from {appCount} app{(appCount == 1 ? "" : "s")}",
                WinForms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tray] Clear Now failed");
            _notifyIcon.ShowBalloonTip(
                5000,
                "SlateClean",
                "Clear failed — see logs",
                WinForms.ToolTipIcon.Error);
        }
    }

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        try
        {
            if (_startupService.IsRunOnStartupEnabled())
            {
                _startupService.DisableRunOnStartup();
            }
            else
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? System.Reflection.Assembly.GetEntryAssembly()?.Location
                    ?? string.Empty;
                _startupService.EnableRunOnStartup(exePath);
            }
            _startupItem.Checked = _startupService.IsRunOnStartupEnabled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tray] Failed to toggle startup");
        }
    }

    private void OnThresholdBreached(object? sender, DiskThresholdBreachedEventArgs e)
    {
        var freeGb = e.FreeBytes / 1024.0 / 1024.0 / 1024.0;
        var thresholdGb = e.ThresholdBytes / 1024.0 / 1024.0 / 1024.0;
        _notifyIcon.ShowBalloonTip(
            10000,
            "SlateClean — Low disk space",
            $"Free space {freeGb:F1} GB is below threshold {thresholdGb:F0} GB",
            WinForms.ToolTipIcon.Warning);
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        if (!_settings.IsVisible)
        {
            _settings.Show();
        }
        if (_settings.WindowState == WindowState.Minimized)
        {
            _settings.WindowState = WindowState.Normal;
        }
        _settings.Activate();
        _settings.Topmost = true;
        _settings.Topmost = false;
        _settings.Focus();
    }

    private void OnExit(object? sender, EventArgs e)
    {
        // Hide the icon synchronously BEFORE Shutdown() so it cannot ghost
        // in the tray while the async OnExit handler unwinds the Host.
        _notifyIcon.Visible = false;
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _diskMonitor.DiskThresholdBreached -= OnThresholdBreached;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}

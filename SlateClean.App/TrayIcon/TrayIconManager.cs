using System.Diagnostics;
using System.Drawing;
using System.Windows;
using Microsoft.Extensions.Logging;
using SlateClean.App.ViewModels;
using SlateClean.App.Views;
using SlateClean.Core.Models;
using SlateClean.Core.Services;
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
    private readonly CleanupReviewWindow _reviewWindow;
    private readonly CleanupReviewViewModel _reviewVm;
    private readonly AutoCleanCoordinator _coordinator;
    private readonly ILogger<TrayIconManager> _logger;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _startupItem;
    private CleanupPlan? _pendingPlan;

    public TrayIconManager(
        CacheLocator cacheLocator,
        CleanupService cleanupService,
        DiskMonitorService diskMonitor,
        StartupService startupService,
        DashboardWindow dashboard,
        SettingsWindow settings,
        CleanupReviewWindow reviewWindow,
        CleanupReviewViewModel reviewVm,
        AutoCleanCoordinator coordinator,
        ILogger<TrayIconManager> logger)
    {
        _cacheLocator = cacheLocator;
        _cleanupService = cleanupService;
        _diskMonitor = diskMonitor;
        _startupService = startupService;
        _dashboard = dashboard;
        _settings = settings;
        _reviewWindow = reviewWindow;
        _reviewVm = reviewVm;
        _coordinator = coordinator;
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
        _notifyIcon.BalloonTipClicked += OnReviewBalloonClicked;

        _diskMonitor.DiskThresholdBreached += OnThresholdBreached;
        _coordinator.PlanBuilt += OnPlanBuilt;
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

    // Soft-tier PlanBuilt → tray balloon "review proposed cleanup". Clicking
    // the balloon opens the review window. Critical tier stays log-only here;
    // it gets silent execution wiring in slice 6d.
    private void OnPlanBuilt(object? sender, CleanupPlan plan)
    {
        if (plan.Tier != BreachTier.SoftThreshold) return;
        if (plan.TotalFiles == 0)
        {
            _logger.LogInformation("[Tray] Soft breach but no eligible files — suppressing review prompt");
            return;
        }

        _pendingPlan = plan;
        var gb = plan.TotalBytes / 1024.0 / 1024.0 / 1024.0;
        _notifyIcon.ShowBalloonTip(
            10000,
            "SlateClean — Cleanup recommended",
            $"{plan.TotalFiles} files ({gb:F2} GB) eligible. Click to review.",
            WinForms.ToolTipIcon.Info);
    }

    private void OnReviewBalloonClicked(object? sender, EventArgs e)
    {
        var plan = _pendingPlan;
        if (plan is null) return;
        _pendingPlan = null;

        // PlanBuilt fires on a worker thread; the balloon callback should be on
        // the UI thread already, but marshal explicitly to be safe before
        // touching WPF state.
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _reviewVm.Load(plan);
            if (!_reviewWindow.IsVisible) _reviewWindow.Show();
            if (_reviewWindow.WindowState == WindowState.Minimized)
                _reviewWindow.WindowState = WindowState.Normal;
            _reviewWindow.Activate();
            _reviewWindow.Topmost = true;
            _reviewWindow.Topmost = false;
            _reviewWindow.Focus();
        });
    }

    private void OnThresholdBreached(object? sender, DiskThresholdBreachedEventArgs e)
    {
        // Both tiers are now owned downstream of AutoCleanCoordinator:
        //   Soft     → PlanBuilt → OnPlanBuilt → "Cleanup recommended" balloon
        //   Critical → silent execution (no balloon, no Review window) per
        //              Slice 6d. A post-cleanup notification banner is
        //              tracked as a separate backlog item.
        // The Phase 1 generic "Low disk space" balloon is therefore dormant.
        // Subscription is kept so future slices (e.g. a Focus-Assist banner
        // path) can wire onto the same handler without re-plumbing events.
        _logger.LogDebug("[Tray] Threshold breach event observed ({Tier}); no generic balloon", e.Tier);
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
        _coordinator.PlanBuilt -= OnPlanBuilt;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}

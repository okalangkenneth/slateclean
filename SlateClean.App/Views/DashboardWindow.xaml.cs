using System.ComponentModel;
using System.Windows;
using SlateClean.App.ViewModels;

namespace SlateClean.App.Views;

public partial class DashboardWindow : Window
{
    public DashboardWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Tray-driven lifetime: the X button hides the window so the singleton
        // DI instance survives. Application exits only via the tray "Exit" item,
        // which calls Application.Shutdown() directly.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}

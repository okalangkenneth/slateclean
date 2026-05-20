using System.ComponentModel;
using System.Windows;
using SlateClean.App.ViewModels;

namespace SlateClean.App.Views;

public partial class CleanupReviewWindow : Window
{
    public CleanupReviewWindow(CleanupReviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (_, _) => Hide();
    }

    // Parameterless ctor for the XAML smoke test.
    internal CleanupReviewWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Tray-driven lifetime — X hides the singleton so the next breach can
        // reuse it. The user's choice on the visible plan was "do nothing".
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}

using System.ComponentModel;
using System.Windows;
using SlateClean.App.ViewModels;

namespace SlateClean.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel? _vm;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        viewModel.RequestClose += OnRequestClose;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    // Parameterless ctor for the XAML smoke test, which supplies a stub VM
    // via DataContext rather than constructing real services.
    internal SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnRequestClose(object? sender, EventArgs e) => Hide();

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Discard any in-flight edits and reload from storage every time the
        // window is re-opened. Pairs with the singleton VM lifetime: one VM
        // instance across opens, but always starting from persisted state.
        if (e.NewValue is true)
        {
            _vm?.Reload();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Tray-driven lifetime: closing via X hides instead of disposing, so
        // the singleton DI instance survives. Treat X as Cancel — drop edits.
        e.Cancel = true;
        _vm?.Reload();
        Hide();
        base.OnClosing(e);
    }
}

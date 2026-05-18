using CommunityToolkit.Mvvm.ComponentModel;

namespace SlateClean.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusLine = "Dashboard ready.";
}

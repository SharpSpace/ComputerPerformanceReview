using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ComputerPerformanceReview.ViewModels;

public partial class StartupViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty]
    private int _monitorMinutes = 10;

    [ObservableProperty]
    private bool _isAdmin = App.IsAdmin;

    public StartupViewModel(MainViewModel main)
    {
        _main = main;
    }

    [RelayCommand]
    private void StartMonitor()
    {
        _main.NavigateToMonitor(MonitorMinutes);
    }

    [RelayCommand]
    private void RunSnapshot()
    {
        _main.NavigateToSnapshot();
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        App.RestartAsAdmin();
    }
}

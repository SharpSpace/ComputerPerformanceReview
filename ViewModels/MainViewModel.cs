using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ComputerPerformanceReview.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private string _title = "System Performance Monitor";

    public MainViewModel()
    {
        CurrentView = new StartupViewModel(this);
    }

    public void NavigateToMonitor(int durationMinutes)
    {
        var vm = new MonitorViewModel(this, durationMinutes);
        CurrentView = vm;
        vm.Start();
    }

    public void NavigateToSnapshot()
    {
        var vm = new SnapshotViewModel(this);
        CurrentView = vm;
        _ = vm.RunAsync();
    }

    public void NavigateToStartup()
    {
        // Stop any running monitor
        if (CurrentView is MonitorViewModel monitor)
            monitor.Stop();

        CurrentView = new StartupViewModel(this);
    }
}

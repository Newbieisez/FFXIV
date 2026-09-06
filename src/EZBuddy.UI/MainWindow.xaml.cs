using System.Windows;
using EZBuddy.UI.ViewModels;

namespace EZBuddy.UI;

public partial class MainWindow : Window
{
    public MainWindow(IHostTelemetryProvider? telemetryProvider = null)
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel(telemetryProvider);
        DataContext = ViewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public MainWindowViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
        => ViewModel.StartAutoRefresh();

    private void OnClosed(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        ViewModel.Dispose();
    }
}

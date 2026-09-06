using System.Windows;
using System.Windows.Controls;
using EZBuddy.UI.ViewModels;
using EZBuddy.UI.Views;

namespace EZBuddy.UI;

public partial class MainWindow : Window
{
    private LicenseActivationView? _licenseView;

    public MainWindow(IHostTelemetryProvider? telemetryProvider = null)
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel(telemetryProvider);
        DataContext = ViewModel;
        AttachLicenseWorkspace();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public MainWindowViewModel ViewModel { get; }

    private void AttachLicenseWorkspace()
    {
        if (Content is not Border rootBorder || rootBorder.Child is not Grid rootGrid)
        {
            return;
        }

        var bodyGrid = rootGrid.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) == 1);

        if (bodyGrid is null || bodyGrid.ColumnDefinitions.Count < 2)
        {
            return;
        }

        _licenseView = new LicenseActivationView();
        Grid.SetColumn(_licenseView, 1);
        Panel.SetZIndex(_licenseView, 100);
        bodyGrid.Children.Add(_licenseView);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
        => ViewModel.StartAutoRefresh();

    private void OnClosed(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        ViewModel.Dispose();
        _licenseView = null;
    }
}

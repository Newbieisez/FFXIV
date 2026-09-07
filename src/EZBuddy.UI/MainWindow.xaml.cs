using System.Windows;
using System.Windows.Controls;
using EZBuddy.Core.Bundles;
using EZBuddy.Core.Engine;
using EZBuddy.Core.Settings;
using EZBuddy.UI.ViewModels;
using EZBuddy.UI.Views;

namespace EZBuddy.UI;

public partial class MainWindow : Window
{
    private LicenseActivationView? _licenseView;
    private FirstPlayableLoopView? _firstLoopView;

    public MainWindow(
        IHostTelemetryProvider? telemetryProvider = null,
        IFirstPlayableLoopController? firstLoopController = null,
        IObservableSettings? settings = null,
        IRunLoopController? runLoopController = null)
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel(
            telemetryProvider,
            firstLoopController,
            settings,
            runLoopController);
        DataContext = ViewModel;
        AttachOverlayWorkspaces();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public MainWindowViewModel ViewModel { get; }

    private void AttachOverlayWorkspaces()
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

        _firstLoopView = new FirstPlayableLoopView();
        Grid.SetColumn(_firstLoopView, 1);
        Panel.SetZIndex(_firstLoopView, 99);
        bodyGrid.Children.Add(_firstLoopView);

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
        _firstLoopView = null;
    }
}

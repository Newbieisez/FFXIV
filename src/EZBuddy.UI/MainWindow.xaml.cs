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
    private ProductIntelligenceWindow? _productIntelligenceWindow;

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
        AttachProductIntelligenceButton();
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

    private void AttachProductIntelligenceButton()
    {
        if (Content is not Border rootBorder || rootBorder.Child is not Grid rootGrid)
        {
            return;
        }

        var headerBorder = rootGrid.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 0);

        if (headerBorder?.Child is not Grid headerGrid)
        {
            return;
        }

        var actionPanel = headerGrid.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetColumn(panel) == 2);

        if (actionPanel is null)
        {
            return;
        }

        var intelligenceButton = new Button
        {
            Content = "◆ Intelligence",
            ToolTip = "Open Preflight, Dry Run, Smart Gear, recovery guidance, and goal planning.",
            Margin = new Thickness(0, 0, 7, 0)
        };
        intelligenceButton.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(intelligenceButton, true);
        intelligenceButton.Click += OpenProductIntelligence;
        actionPanel.Children.Insert(0, intelligenceButton);
    }

    private void OpenProductIntelligence(object sender, RoutedEventArgs e)
    {
        if (_productIntelligenceWindow is { IsVisible: true })
        {
            _productIntelligenceWindow.Activate();
            return;
        }

        _productIntelligenceWindow = new ProductIntelligenceWindow
        {
            Owner = this
        };
        _productIntelligenceWindow.Closed += (_, _) => _productIntelligenceWindow = null;
        _productIntelligenceWindow.Show();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
        => ViewModel.StartAutoRefresh();

    private void OnClosed(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        _productIntelligenceWindow?.Close();
        _productIntelligenceWindow = null;
        ViewModel.Dispose();
        _licenseView = null;
        _firstLoopView = null;
    }
}

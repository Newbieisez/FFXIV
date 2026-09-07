using System.Windows;
using System.Windows.Controls;

namespace EZBuddy.UI.Views;

public partial class FirstPlayableLoopView : UserControl
{
    public FirstPlayableLoopView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (Content is not Border border ||
            border.Child is not ScrollViewer scrollViewer ||
            scrollViewer.Content is not StackPanel rootStack)
        {
            return;
        }

        var button = new Button
        {
            Content = "◆ Product Intelligence / Dry Run",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Run preflight, compatibility checks, dry-run simulation, and goal planning without starting the queue."
        };
        button.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        button.Click += OpenProductIntelligence;
        rootStack.Children.Insert(0, button);
    }

    private void OpenProductIntelligence(object sender, RoutedEventArgs e)
    {
        var window = new ProductIntelligenceWindow
        {
            Owner = Window.GetWindow(this)
        };
        window.Show();
    }
}

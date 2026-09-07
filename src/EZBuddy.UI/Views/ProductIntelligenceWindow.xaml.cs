using System.Windows;
using EZBuddy.UI.ViewModels;

namespace EZBuddy.UI.Views;

public partial class ProductIntelligenceWindow : Window
{
    public ProductIntelligenceWindow(IProductIntelligenceProvider? provider = null)
    {
        InitializeComponent();
        ViewModel = new ProductIntelligenceViewModel(provider);
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    public ProductIntelligenceViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            await ViewModel.RefreshAsync().ConfigureAwait(true);
        }
        catch
        {
        }
    }
}

using System.ComponentModel;
using System.Windows;
using DeliveryOps.PosBridge.Wpf.ViewModels;

namespace DeliveryOps.PosBridge.Wpf;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow() => InitializeComponent();

    public void SetViewModel(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            await viewModel.InitializeAsync();
            SecretBox.Password = viewModel.Secret;
        };
    }

    private void SecretBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) _viewModel.Secret = SecretBox.Password;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.StopAsync();
        base.OnClosing(e);
    }
}

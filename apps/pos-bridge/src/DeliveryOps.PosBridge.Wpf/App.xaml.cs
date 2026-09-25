using System.Windows;
using System.Net.Http;
using DeliveryOps.PosBridge.Core.Services;
using DeliveryOps.PosBridge.Wpf.Services;
using DeliveryOps.PosBridge.Wpf.ViewModels;

namespace DeliveryOps.PosBridge.Wpf;

public partial class App : Application
{
    private HttpClient? _httpClient;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        PosBridgeProcessor processor = new(new FolderOrderQueue(), new WebhookOrderSender(_httpClient));
        MainViewModel viewModel = new(new BridgeSettingsStore(new WindowsSecretProtector()), processor);
        MainWindow window = new();
        window.SetViewModel(viewModel);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _httpClient?.Dispose();
        base.OnExit(e);
    }
}

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
        // The cashier screen must stay open during service: report unexpected UI errors instead of closing.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Beklenmeyen bir hata oluştu: {args.Exception.Message}", "DeliveryOps Kasa",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        WebhookOrderSender sender = new(_httpClient);
        PosBridgeProcessor processor = new(new FolderOrderQueue(), sender);
        string dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeliveryOps", "PosBridge");
        CashierOrderJournal journal = new(Path.Combine(dataDirectory, "orders.json"));
        MenuViewModel menu = new(new MenuStore(Path.Combine(dataDirectory, "menu.json")));
        MainViewModel viewModel = new(new BridgeSettingsStore(new WindowsSecretProtector()), processor, menu,
            settings => new CashierViewModel(new CashierService(sender, journal), journal,
                new ManualPaymentTerminal(), settings));
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

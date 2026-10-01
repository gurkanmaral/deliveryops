using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using DeliveryOps.PosBridge.Core.Models;
using DeliveryOps.PosBridge.Core.Services;
using DeliveryOps.PosBridge.Wpf.Services;

namespace DeliveryOps.PosBridge.Wpf.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly BridgeSettingsStore _settingsStore;
    private readonly PosBridgeProcessor _processor;
    private CancellationTokenSource? _runningCancellation;
    private string _webhookUrl = string.Empty, _secret = string.Empty, _inboxDirectory = string.Empty;
    private string _pollIntervalSeconds = "5", _validationMessage = string.Empty, _lastCycleText = "Henüz kontrol edilmedi";
    private bool _isRunning;

    public MainViewModel(BridgeSettingsStore settingsStore, PosBridgeProcessor processor, MenuViewModel menu,
        Func<Func<BridgeRuntimeSettings?>, CashierViewModel> createCashier)
    {
        _settingsStore = settingsStore;
        _processor = processor;
        Menu = menu;
        Cashier = createCashier(CurrentSettings);
        Menu.MenuChanged += Cashier.ApplyMenu;
        _processor.LogReceived += (_, entry) => Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Insert(0, entry);
            while (Logs.Count > 250) Logs.RemoveAt(Logs.Count - 1);
        });
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsRunning);
        StopCommand = new AsyncRelayCommand(StopAsync, () => IsRunning);
        ProcessNowCommand = new AsyncRelayCommand(ProcessNowAsync, () => !IsRunning);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public MenuViewModel Menu { get; }
    public CashierViewModel Cashier { get; }
    public ObservableCollection<BridgeLogEntry> Logs { get; } = [];
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand ProcessNowCommand { get; }
    public string WebhookUrl { get => _webhookUrl; set => Set(ref _webhookUrl, value); }
    public string Secret { get => _secret; set => Set(ref _secret, value); }
    public string InboxDirectory { get => _inboxDirectory; set { if (Set(ref _inboxDirectory, value)) OnPropertyChanged(nameof(PendingFileText)); } }
    public string PollIntervalSeconds { get => _pollIntervalSeconds; set => Set(ref _pollIntervalSeconds, value); }
    public string ValidationMessage { get => _validationMessage; private set => Set(ref _validationMessage, value); }
    public string LastCycleText { get => _lastCycleText; private set => Set(ref _lastCycleText, value); }
    public bool IsRunning { get => _isRunning; private set { if (Set(ref _isRunning, value)) { OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusDetail)); RaiseCommands(); } } }
    public string StatusText => IsRunning ? "Bridge çalışıyor" : "Bridge durduruldu";
    public string StatusDetail => IsRunning ? $"Klasör her {PollIntervalSeconds} saniyede kontrol ediliyor." : "Sipariş aktarımı şu anda kapalı.";
    public string PendingFileText => Directory.Exists(InboxDirectory) ? $"Bekleyen dosya: {Directory.EnumerateFiles(InboxDirectory, "*.json").Count()}" : "Bekleyen dosya: 0";

    public async Task InitializeAsync()
    {
        BridgeRuntimeSettings settings = await _settingsStore.LoadAsync();
        WebhookUrl = settings.WebhookUrl; Secret = settings.Secret; InboxDirectory = settings.InboxDirectory;
        PollIntervalSeconds = settings.PollIntervalSeconds.ToString();
        await Menu.LoadAsync();
        await Cashier.LoadOrdersAsync();
        // Orders queued while offline are only delivered by the folder processor, so keep it running.
        if (CurrentSettings() is not null && StartCommand.CanExecute(null)) StartCommand.Execute(null);
    }

    /// <summary>The saved connection settings, or null when they are incomplete.</summary>
    private BridgeRuntimeSettings? CurrentSettings()
    {
        int interval = int.TryParse(PollIntervalSeconds, out int parsed) ? parsed : 0;
        BridgeRuntimeSettings settings = new(WebhookUrl.Trim(), Secret.Trim(), InboxDirectory.Trim(), interval);
        return settings.Validate().Count == 0 ? settings : null;
    }

    public async Task StopAsync()
    {
        if (_runningCancellation is null) return;
        await _runningCancellation.CancelAsync();
        _runningCancellation.Dispose();
        _runningCancellation = null;
        IsRunning = false;
        Logs.Insert(0, new BridgeLogEntry(DateTimeOffset.Now, BridgeLogLevel.Information, "Bridge stopped."));
    }

    private async Task SaveAsync()
    {
        BridgeRuntimeSettings? settings = BuildSettings();
        if (settings is null) return;
        await _settingsStore.SaveAsync(settings);
        ValidationMessage = string.Empty;
        Logs.Insert(0, new BridgeLogEntry(DateTimeOffset.Now, BridgeLogLevel.Success, "Settings saved securely for the current Windows user."));
    }

    private async Task StartAsync()
    {
        BridgeRuntimeSettings? settings = BuildSettings();
        if (settings is null) return;
        await _settingsStore.SaveAsync(settings);
        _runningCancellation = new CancellationTokenSource();
        IsRunning = true;
        _ = RunLoopAsync(settings, _runningCancellation.Token);
    }

    private async Task ProcessNowAsync()
    {
        BridgeRuntimeSettings? settings = BuildSettings();
        if (settings is null) return;
        await ProcessCycleAsync(settings, CancellationToken.None);
    }

    private async Task RunLoopAsync(BridgeRuntimeSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // One failed cycle (a file locked by antivirus, the inbox briefly unreachable) must not stop
                // the bridge: queued orders would silently stay on this computer.
                try { await ProcessCycleAsync(settings, cancellationToken); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    ValidationMessage = exception.Message;
                    Logs.Insert(0, new BridgeLogEntry(DateTimeOffset.Now, BridgeLogLevel.Error,
                        $"Kontrol başarısız, tekrar denenecek: {exception.Message}"));
                }
                await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task ProcessCycleAsync(BridgeRuntimeSettings settings, CancellationToken cancellationToken)
    {
        ProcessingCycleResult result = await _processor.ProcessOnceAsync(settings, cancellationToken);
        LastCycleText = $"Son kontrol {DateTime.Now:HH:mm:ss} • {result.Processed} gönderildi, {result.Retrying} bekliyor, {result.Rejected} reddedildi";
        OnPropertyChanged(nameof(PendingFileText));
    }

    private BridgeRuntimeSettings? BuildSettings()
    {
        int interval = int.TryParse(PollIntervalSeconds, out int parsed) ? parsed : 0;
        BridgeRuntimeSettings settings = new(WebhookUrl.Trim(), Secret.Trim(), InboxDirectory.Trim(), interval);
        IReadOnlyList<string> errors = settings.Validate();
        ValidationMessage = string.Join(" ", errors);
        return errors.Count == 0 ? settings : null;
    }

    private void RaiseCommands()
    {
        StartCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged(); ProcessNowCommand.RaiseCanExecuteChanged();
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(propertyName); return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

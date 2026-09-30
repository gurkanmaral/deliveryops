using System.Collections.ObjectModel;
using System.Globalization;
using DeliveryOps.PosBridge.Core.Abstractions;
using DeliveryOps.PosBridge.Core.Models;
using DeliveryOps.PosBridge.Core.Services;

namespace DeliveryOps.PosBridge.Wpf.ViewModels;

public sealed record PaymentOptionItem(CashierPaymentOption Option, string Label, bool IsTakenAtCounter);

public sealed class CashierOrderRow(CashierOrderRecord record)
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    public CashierOrderRecord Record { get; } = record;
    public string Title => $"{Record.ExternalOrderId} • {Record.CustomerName}";
    public string Time => Record.CreatedAt.ToLocalTime().ToString("HH:mm", Turkish);
    public string Total => Record.TotalAmount.ToString("C2", Turkish);
    public string Items => Record.ItemsSummary;
    public string PaymentText => Record.IsPaid
        ? $"Ödendi ({MethodLabel(Record.PaymentMethod)})"
        : Record.PaymentMethod switch
        {
            "cash" => "Kapıda nakit alınacak",
            "card" => "Kapıda kart ile alınacak",
            _ => "Ödeme bekleniyor"
        };
    public string SyncText => Record.SyncState == CashierOrderSyncState.Queued ? "Gönderim kuyrukta" : "Panele iletildi";
    public bool CanMarkPaid => !Record.IsPaid;

    private static string MethodLabel(string method) => method switch
    {
        "cash" => "nakit",
        "card" => "kart",
        "online" => "online",
        _ => method
    };
}

/// <summary>
/// Counter screen: pick products from the local menu, choose how the customer pays, send the order to
/// DeliveryOps and report payments taken on the NarPOS device so they show up on the panel.
/// </summary>
public sealed class CashierViewModel : ObservableObject
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly CashierService _cashier;
    private readonly CashierOrderJournal _journal;
    private readonly IPaymentTerminal _terminal;
    private readonly Func<BridgeRuntimeSettings?> _settings;
    private readonly CashierCart _cart = new();
    private MenuCatalog _catalog = MenuCatalog.Empty;
    private MenuCategory? _selectedCategory;
    private PaymentOptionItem _selectedPaymentOption;
    private string _customerName = string.Empty, _customerPhone = string.Empty, _customerAddress = string.Empty;
    private string _customerNote = string.Empty, _paymentReference = string.Empty, _message = string.Empty;
    private string _laterPaymentReference = string.Empty;
    private bool _isPickup, _isBusy;

    public CashierViewModel(CashierService cashier, CashierOrderJournal journal, IPaymentTerminal terminal,
        Func<BridgeRuntimeSettings?> settings)
    {
        _cashier = cashier;
        _journal = journal;
        _terminal = terminal;
        _settings = settings;
        _selectedPaymentOption = PaymentOptions[0];
        AddProductCommand = new ParameterCommand<MenuProduct>(product => { AddToCart(product); return Task.CompletedTask; });
        IncreaseLineCommand = new ParameterCommand<CartLine>(line => { ChangeQuantity(line, +1); return Task.CompletedTask; });
        DecreaseLineCommand = new ParameterCommand<CartLine>(line => { ChangeQuantity(line, -1); return Task.CompletedTask; });
        ClearCartCommand = new AsyncRelayCommand(() => { _cart.Clear(); RefreshCart(); return Task.CompletedTask; });
        SubmitCommand = new AsyncRelayCommand(SubmitAsync, () => !IsBusy);
        MarkPaidCashCommand = new ParameterCommand<CashierOrderRow>(row => MarkPaidAsync(row, "cash"));
        MarkPaidCardCommand = new ParameterCommand<CashierOrderRow>(row => MarkPaidAsync(row, "card"));
        RefreshOrdersCommand = new AsyncRelayCommand(LoadOrdersAsync);
    }

    public IReadOnlyList<PaymentOptionItem> PaymentOptions { get; } =
    [
        new(CashierPaymentOption.PaidByCardAtCounter, "Kasada kart (NarPOS)", true),
        new(CashierPaymentOption.PaidByCashAtCounter, "Kasada nakit", true),
        new(CashierPaymentOption.CashOnDelivery, "Kapıda nakit", false),
        new(CashierPaymentOption.CardOnDelivery, "Kapıda kart", false),
        new(CashierPaymentOption.PaidOnline, "Online ödendi", true)
    ];

    public ObservableCollection<MenuCategory> Categories { get; } = [];
    public ObservableCollection<MenuProduct> Products { get; } = [];
    public ObservableCollection<CartLine> CartLines { get; } = [];
    public ObservableCollection<CashierOrderRow> RecentOrders { get; } = [];
    public ParameterCommand<MenuProduct> AddProductCommand { get; }
    public ParameterCommand<CartLine> IncreaseLineCommand { get; }
    public ParameterCommand<CartLine> DecreaseLineCommand { get; }
    public AsyncRelayCommand ClearCartCommand { get; }
    public AsyncRelayCommand SubmitCommand { get; }
    public ParameterCommand<CashierOrderRow> MarkPaidCashCommand { get; }
    public ParameterCommand<CashierOrderRow> MarkPaidCardCommand { get; }
    public AsyncRelayCommand RefreshOrdersCommand { get; }

    public MenuCategory? SelectedCategory
    {
        get => _selectedCategory;
        set { if (Set(ref _selectedCategory, value)) RefreshProducts(); }
    }

    public PaymentOptionItem SelectedPaymentOption
    {
        get => _selectedPaymentOption;
        set { if (Set(ref _selectedPaymentOption, value)) OnPropertyChanged(nameof(ShowPaymentReference)); }
    }

    public string CustomerName { get => _customerName; set => Set(ref _customerName, value); }
    public string CustomerPhone { get => _customerPhone; set => Set(ref _customerPhone, value); }
    public string CustomerAddress { get => _customerAddress; set => Set(ref _customerAddress, value); }
    public string CustomerNote { get => _customerNote; set => Set(ref _customerNote, value); }
    public string PaymentReference { get => _paymentReference; set => Set(ref _paymentReference, value); }
    public string LaterPaymentReference { get => _laterPaymentReference; set => Set(ref _laterPaymentReference, value); }
    public bool IsPickup { get => _isPickup; set { if (Set(ref _isPickup, value)) OnPropertyChanged(nameof(IsDelivery)); } }
    public bool IsDelivery => !IsPickup;
    public bool ShowPaymentReference => SelectedPaymentOption.IsTakenAtCounter;
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) SubmitCommand.RaiseCanExecuteChanged(); } }
    public string TotalText => _cart.Total.ToString("C2", Turkish);
    public string TerminalName => _terminal.Name;
    public bool HasMenu => _catalog.Products.Count > 0;
    public bool ShowMenuHint => !HasMenu;

    public void ApplyMenu(MenuCatalog catalog)
    {
        _catalog = catalog;
        Guid? selectedId = SelectedCategory?.Id;
        Categories.Clear();
        foreach (MenuCategory category in catalog.Categories.OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
            Categories.Add(category);
        _selectedCategory = Categories.FirstOrDefault(x => x.Id == selectedId) ?? Categories.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(HasMenu));
        OnPropertyChanged(nameof(ShowMenuHint));
        RefreshProducts();
    }

    public async Task LoadOrdersAsync()
    {
        IReadOnlyList<CashierOrderRecord> records;
        try { records = await _journal.LoadAsync(); }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            Message = $"Son siparişler okunamadı: {exception.Message}";
            return;
        }
        RecentOrders.Clear();
        foreach (CashierOrderRecord record in records.Take(50)) RecentOrders.Add(new CashierOrderRow(record));
    }

    private void AddToCart(MenuProduct product)
    {
        try { _cart.Add(product); }
        catch (InvalidOperationException exception) { Message = exception.Message; return; }
        RefreshCart();
    }

    private void ChangeQuantity(CartLine line, int delta)
    {
        CartLine? current = _cart.Lines.FirstOrDefault(x => x.ProductId == line.ProductId);
        if (current is null) return;
        _cart.SetQuantity(line.ProductId, current.Quantity + delta);
        RefreshCart();
    }

    private async Task SubmitAsync()
    {
        BridgeRuntimeSettings? settings = _settings();
        if (settings is null) { Message = "Önce Bağlantı sekmesinde webhook ayarlarını tamamlayın."; return; }
        IsBusy = true;
        try
        {
            CashierResult result = await _cashier.SubmitAsync(settings, _cart,
                new CashierCustomer(CustomerName, CustomerPhone, CustomerAddress, CustomerNote),
                SelectedPaymentOption.Option, IsPickup, PaymentReference, CancellationToken.None);
            Message = result.Message;
            if (!result.Success) return;
            _cart.Clear();
            RefreshCart();
            CustomerName = CustomerPhone = CustomerAddress = CustomerNote = PaymentReference = string.Empty;
            await LoadOrdersAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            Message = $"Sipariş kaydedilemedi: {exception.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task MarkPaidAsync(CashierOrderRow row, string method)
    {
        BridgeRuntimeSettings? settings = _settings();
        if (settings is null) { Message = "Önce Bağlantı sekmesinde webhook ayarlarını tamamlayın."; return; }
        try
        {
            CashierResult result = await _cashier.MarkPaidAsync(settings, row.Record, method, LaterPaymentReference,
                CancellationToken.None);
            Message = result.Message;
            if (result.Success)
            {
                LaterPaymentReference = string.Empty;
                await LoadOrdersAsync();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            Message = $"Ödeme kaydedilemedi: {exception.Message}";
        }
    }

    private void RefreshProducts()
    {
        Products.Clear();
        if (SelectedCategory is null) return;
        foreach (MenuProduct product in _catalog.Products
                     .Where(x => x.CategoryId == SelectedCategory.Id && x.IsAvailable)
                     .OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
            Products.Add(product);
    }

    private void RefreshCart()
    {
        CartLines.Clear();
        foreach (CartLine line in _cart.Lines) CartLines.Add(line);
        OnPropertyChanged(nameof(TotalText));
    }
}

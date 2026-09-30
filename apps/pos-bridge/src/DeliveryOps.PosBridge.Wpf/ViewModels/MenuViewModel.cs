using System.Collections.ObjectModel;
using System.Globalization;
using DeliveryOps.PosBridge.Core.Models;
using DeliveryOps.PosBridge.Core.Services;

namespace DeliveryOps.PosBridge.Wpf.ViewModels;

/// <summary>Menu editor. The menu is stored only on this PC (menu.json) and is not sent to DeliveryOps.</summary>
public sealed class MenuViewModel : ObservableObject
{
    private readonly MenuStore _store;
    private MenuCatalog _catalog = MenuCatalog.Empty;
    private MenuCategory? _selectedCategory;
    private MenuProduct? _selectedProduct;
    private string _newCategoryName = string.Empty, _productName = string.Empty, _productPrice = string.Empty;
    private string _productDescription = string.Empty, _message = string.Empty;
    private bool _productAvailable = true;

    public MenuViewModel(MenuStore store)
    {
        _store = store;
        AddCategoryCommand = new AsyncRelayCommand(AddCategoryAsync);
        DeleteCategoryCommand = new AsyncRelayCommand(DeleteCategoryAsync, () => SelectedCategory is not null);
        SaveProductCommand = new AsyncRelayCommand(SaveProductAsync, () => SelectedCategory is not null);
        NewProductCommand = new AsyncRelayCommand(() => { SelectedProduct = null; ClearProductForm(); return Task.CompletedTask; });
        DeleteProductCommand = new AsyncRelayCommand(DeleteProductAsync, () => SelectedProduct is not null);
        ToggleAvailabilityCommand = new ParameterCommand<MenuProduct>(ToggleAvailabilityAsync);
    }

    public event Action<MenuCatalog>? MenuChanged;
    public ObservableCollection<MenuCategory> Categories { get; } = [];
    public ObservableCollection<MenuProduct> Products { get; } = [];
    public AsyncRelayCommand AddCategoryCommand { get; }
    public AsyncRelayCommand DeleteCategoryCommand { get; }
    public AsyncRelayCommand SaveProductCommand { get; }
    public AsyncRelayCommand NewProductCommand { get; }
    public AsyncRelayCommand DeleteProductCommand { get; }
    public ParameterCommand<MenuProduct> ToggleAvailabilityCommand { get; }
    public MenuCatalog Catalog => _catalog;

    public MenuCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!Set(ref _selectedCategory, value)) return;
            RefreshProducts();
            SelectedProduct = null;
            ClearProductForm();
            DeleteCategoryCommand.RaiseCanExecuteChanged();
            SaveProductCommand.RaiseCanExecuteChanged();
        }
    }

    public MenuProduct? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (!Set(ref _selectedProduct, value)) return;
            if (value is not null)
            {
                ProductName = value.Name;
                ProductPrice = value.Price.ToString("0.00", CultureInfo.GetCultureInfo("tr-TR"));
                ProductDescription = value.Description ?? string.Empty;
                ProductAvailable = value.IsAvailable;
            }
            OnPropertyChanged(nameof(ProductFormTitle));
            DeleteProductCommand.RaiseCanExecuteChanged();
        }
    }

    public string NewCategoryName { get => _newCategoryName; set => Set(ref _newCategoryName, value); }
    public string ProductName { get => _productName; set => Set(ref _productName, value); }
    public string ProductPrice { get => _productPrice; set => Set(ref _productPrice, value); }
    public string ProductDescription { get => _productDescription; set => Set(ref _productDescription, value); }
    public bool ProductAvailable { get => _productAvailable; set => Set(ref _productAvailable, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string ProductFormTitle => SelectedProduct is null ? "Yeni ürün" : "Ürünü düzenle";

    public async Task LoadAsync()
    {
        try { _catalog = await _store.LoadAsync(); }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            _catalog = MenuCatalog.Empty;
            Message = $"Menü dosyası okunamadı: {exception.Message}";
        }
        RefreshCategories();
        MenuChanged?.Invoke(_catalog);
    }

    private async Task AddCategoryAsync()
    {
        string name = NewCategoryName.Trim();
        if (name.Length == 0) { Message = "Kategori adı girin."; return; }
        MenuCategory category = new(Guid.NewGuid(), name, _catalog.Categories.Count);
        if (await TrySaveAsync(_catalog.WithCategory(category)))
        {
            NewCategoryName = string.Empty;
            SelectedCategory = Categories.FirstOrDefault(x => x.Id == category.Id);
        }
    }

    private async Task DeleteCategoryAsync()
    {
        if (SelectedCategory is null) return;
        if (await TrySaveAsync(_catalog.WithoutCategory(SelectedCategory.Id))) SelectedCategory = Categories.FirstOrDefault();
    }

    private async Task SaveProductAsync()
    {
        if (SelectedCategory is null) { Message = "Önce bir kategori seçin."; return; }
        if (!TryParsePrice(ProductPrice, out decimal price)) { Message = "Fiyatı 125,50 biçiminde girin."; return; }
        MenuProduct product = SelectedProduct is null
            ? new MenuProduct(Guid.NewGuid(), SelectedCategory.Id, ProductName.Trim(), price, ProductAvailable,
                NormalizeOptional(ProductDescription), Products.Count)
            : SelectedProduct with
            {
                Name = ProductName.Trim(), Price = price, IsAvailable = ProductAvailable,
                Description = NormalizeOptional(ProductDescription)
            };
        if (await TrySaveAsync(_catalog.WithProduct(product)))
        {
            SelectedProduct = null;
            ClearProductForm();
        }
    }

    private async Task DeleteProductAsync()
    {
        if (SelectedProduct is null) return;
        if (await TrySaveAsync(_catalog.WithoutProduct(SelectedProduct.Id)))
        {
            SelectedProduct = null;
            ClearProductForm();
        }
    }

    private Task ToggleAvailabilityAsync(MenuProduct product) =>
        TrySaveAsync(_catalog.WithProduct(product with { IsAvailable = !product.IsAvailable }));

    private async Task<bool> TrySaveAsync(MenuCatalog catalog)
    {
        IReadOnlyList<string> errors = catalog.Validate();
        if (errors.Count > 0) { Message = string.Join(" ", errors); return false; }
        try { await _store.SaveAsync(catalog); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Message = $"Menü kaydedilemedi: {exception.Message}";
            return false;
        }
        _catalog = catalog;
        Message = "Menü kaydedildi.";
        RefreshCategories();
        MenuChanged?.Invoke(_catalog);
        return true;
    }

    private void RefreshCategories()
    {
        Guid? selectedId = SelectedCategory?.Id;
        Categories.Clear();
        foreach (MenuCategory category in _catalog.Categories.OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
            Categories.Add(category);
        _selectedCategory = Categories.FirstOrDefault(x => x.Id == selectedId) ?? Categories.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedCategory));
        RefreshProducts();
        DeleteCategoryCommand.RaiseCanExecuteChanged();
        SaveProductCommand.RaiseCanExecuteChanged();
    }

    private void RefreshProducts()
    {
        Products.Clear();
        if (SelectedCategory is null) return;
        foreach (MenuProduct product in _catalog.Products.Where(x => x.CategoryId == SelectedCategory.Id)
                     .OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
            Products.Add(product);
    }

    private void ClearProductForm()
    {
        ProductName = string.Empty;
        ProductPrice = string.Empty;
        ProductDescription = string.Empty;
        ProductAvailable = true;
        OnPropertyChanged(nameof(ProductFormTitle));
    }

    internal static bool TryParsePrice(string text, out decimal price)
    {
        // Accept both "125,50" and "125.50" but never treat a separator as thousands grouping
        // (tr-TR parsing would read "125.50" as 12550).
        price = 0;
        string value = text.Trim().Replace("₺", string.Empty).Trim().Replace(',', '.');
        if (value.Count(x => x == '.') > 1) return false;
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price) &&
               price >= 0;
    }

    private static string? NormalizeOptional(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

namespace DeliveryOps.PosBridge.Core.Models;

/// <summary>The business's menu. It is kept only on the cashier PC and is never sent to DeliveryOps.</summary>
public sealed record MenuCatalog(IReadOnlyList<MenuCategory> Categories, IReadOnlyList<MenuProduct> Products)
{
    public static MenuCatalog Empty { get; } = new([], []);

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        HashSet<Guid> categoryIds = [];
        HashSet<string> categoryNames = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (MenuCategory category in Categories)
        {
            if (string.IsNullOrWhiteSpace(category.Name)) errors.Add("Kategori adı boş olamaz.");
            else if (category.Name.Length > 80) errors.Add($"'{category.Name}' kategori adı 80 karakteri geçemez.");
            else if (!categoryNames.Add(category.Name.Trim())) errors.Add($"'{category.Name}' kategorisi birden fazla kez tanımlı.");
            if (!categoryIds.Add(category.Id)) errors.Add("Kategori kimlikleri benzersiz olmalıdır.");
        }
        HashSet<Guid> productIds = [];
        foreach (MenuProduct product in Products)
        {
            if (string.IsNullOrWhiteSpace(product.Name)) errors.Add("Ürün adı boş olamaz.");
            else if (product.Name.Length > 120) errors.Add($"'{product.Name}' ürün adı 120 karakteri geçemez.");
            if (product.Price < 0 || product.Price > 1_000_000) errors.Add($"'{product.Name}' fiyatı geçersiz.");
            if (decimal.Round(product.Price, 2) != product.Price) errors.Add($"'{product.Name}' fiyatı en fazla 2 ondalık basamak içerebilir.");
            if (!categoryIds.Contains(product.CategoryId)) errors.Add($"'{product.Name}' ürününün kategorisi bulunamadı.");
            if (!productIds.Add(product.Id)) errors.Add("Ürün kimlikleri benzersiz olmalıdır.");
        }
        return errors;
    }

    public MenuCatalog WithCategory(MenuCategory category) => this with
    {
        Categories = [.. Categories.Where(x => x.Id != category.Id), category]
    };

    public MenuCatalog WithoutCategory(Guid categoryId) => this with
    {
        Categories = [.. Categories.Where(x => x.Id != categoryId)],
        Products = [.. Products.Where(x => x.CategoryId != categoryId)]
    };

    public MenuCatalog WithProduct(MenuProduct product) => this with
    {
        Products = [.. Products.Where(x => x.Id != product.Id), product]
    };

    public MenuCatalog WithoutProduct(Guid productId) => this with
    {
        Products = [.. Products.Where(x => x.Id != productId)]
    };
}

public sealed record MenuCategory(Guid Id, string Name, int SortOrder = 0);

public sealed record MenuProduct(Guid Id, Guid CategoryId, string Name, decimal Price, bool IsAvailable = true,
    string? Description = null, int SortOrder = 0);

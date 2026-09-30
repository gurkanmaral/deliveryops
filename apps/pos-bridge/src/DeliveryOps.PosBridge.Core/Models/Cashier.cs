namespace DeliveryOps.PosBridge.Core.Models;

public sealed record CartLine(Guid ProductId, string Name, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>The cashier's working order. Prices are snapshotted when a product is added.</summary>
public sealed class CashierCart
{
    private readonly List<CartLine> _lines = [];

    public IReadOnlyList<CartLine> Lines => _lines;
    public decimal Total => _lines.Sum(x => x.LineTotal);
    public bool IsEmpty => _lines.Count == 0;

    public void Add(MenuProduct product, int quantity = 1)
    {
        if (!product.IsAvailable) throw new InvalidOperationException($"'{product.Name}' şu anda satışta değil.");
        if (quantity is < 1 or > 99) throw new ArgumentOutOfRangeException(nameof(quantity));
        int index = _lines.FindIndex(x => x.ProductId == product.Id);
        if (index >= 0)
            _lines[index] = _lines[index] with { Quantity = Math.Min(99, _lines[index].Quantity + quantity) };
        else
            _lines.Add(new CartLine(product.Id, product.Name, product.Price, quantity));
    }

    public void SetQuantity(Guid productId, int quantity)
    {
        int index = _lines.FindIndex(x => x.ProductId == productId);
        if (index < 0) return;
        if (quantity <= 0) _lines.RemoveAt(index);
        else _lines[index] = _lines[index] with { Quantity = Math.Min(99, quantity) };
    }

    public void Clear() => _lines.Clear();

    /// <summary>A short "2x Lahmacun, 1x Ayran" line the courier and kitchen can read on the order.</summary>
    public string Summary() => string.Join(", ", _lines.Select(x => $"{x.Quantity}x {x.Name}"));
}

/// <summary>How the customer pays, as chosen at the counter.</summary>
public enum CashierPaymentOption
{
    PaidByCardAtCounter,
    PaidByCashAtCounter,
    CashOnDelivery,
    CardOnDelivery,
    PaidOnline
}

public sealed record CashierCustomer(string Name, string Phone, string Address, string? Note = null);

public enum CashierOrderSyncState { Sent, Queued }

/// <summary>A cashier order kept locally so payments taken later can still be reported.</summary>
public sealed record CashierOrderRecord(
    string ExternalOrderId,
    DateTimeOffset CreatedAt,
    string CustomerName,
    decimal TotalAmount,
    string ItemsSummary,
    string PaymentMethod,
    bool IsPaid,
    string? PaymentReference,
    CashierOrderSyncState SyncState,
    PosOrder Order);

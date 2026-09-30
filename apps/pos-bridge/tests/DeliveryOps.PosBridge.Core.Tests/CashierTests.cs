using DeliveryOps.PosBridge.Core.Abstractions;
using DeliveryOps.PosBridge.Core.Models;
using DeliveryOps.PosBridge.Core.Services;

namespace DeliveryOps.PosBridge.Core.Tests;

public sealed class CashierTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"deliveryops-cashier-{Guid.NewGuid():N}");
    private static readonly MenuCategory Food = new(Guid.NewGuid(), "Yemekler");
    private static readonly MenuProduct Lahmacun = new(Guid.NewGuid(), Food.Id, "Lahmacun", 90m);
    private static readonly MenuProduct Ayran = new(Guid.NewGuid(), Food.Id, "Ayran", 25m);

    [Fact]
    public async Task Menu_is_saved_locally_and_invalid_menus_are_rejected()
    {
        MenuStore store = new(Path.Combine(_directory, "menu.json"));
        MenuCatalog menu = MenuCatalog.Empty.WithCategory(Food).WithProduct(Lahmacun).WithProduct(Ayran);

        await store.SaveAsync(menu);
        MenuCatalog loaded = await store.LoadAsync();

        Assert.Equal(2, loaded.Products.Count);
        MenuCatalog orphan = MenuCatalog.Empty.WithProduct(Lahmacun);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(orphan));
        Assert.Contains(menu.WithProduct(Lahmacun with { Price = -1 }).Validate(), x => x.Contains("fiyatı"));
    }

    [Fact]
    public void Removing_a_category_removes_its_products()
    {
        MenuCatalog menu = MenuCatalog.Empty.WithCategory(Food).WithProduct(Lahmacun).WithoutCategory(Food.Id);

        Assert.Empty(menu.Products);
    }

    [Fact]
    public void Cart_merges_lines_totals_and_rejects_unavailable_products()
    {
        CashierCart cart = new();
        cart.Add(Lahmacun, 2);
        cart.Add(Ayran);
        cart.Add(Lahmacun);

        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(3 * 90m + 25m, cart.Total);
        Assert.Equal("3x Lahmacun, 1x Ayran", cart.Summary());
        Assert.Throws<InvalidOperationException>(() => cart.Add(Ayran with { IsAvailable = false }));
        cart.SetQuantity(Ayran.Id, 0);
        Assert.Single(cart.Lines);
    }

    [Fact]
    public async Task Card_paid_at_counter_is_sent_as_paid_order_with_items_summary()
    {
        RecordingSender sender = new(OrderSendResult.Sent(Guid.NewGuid(), false));
        CashierService service = CreateService(sender);
        CashierCart cart = new();
        cart.Add(Lahmacun, 2);

        CashierResult result = await service.SubmitAsync(Settings(), cart, new("Ada", "05550000000", "Kadıköy"),
            CashierPaymentOption.PaidByCardAtCounter, isPickup: false, "AUTH-1", CancellationToken.None);

        Assert.True(result.Success);
        PosOrder sent = Assert.Single(sender.Sent);
        Assert.Equal("order.created", sent.EventType);
        Assert.Equal(180m, sent.TotalAmount);
        Assert.Equal(new PosPayment("card", "paid", 180m, "AUTH-1", sent.Payment!.PaidAtUtc), sent.Payment);
        Assert.Contains("2x Lahmacun", sent.DeliveryInstructions);
        Assert.Equal(CashierOrderSyncState.Sent, result.Record!.SyncState);
    }

    [Fact]
    public async Task Offline_order_is_queued_to_inbox_and_later_payment_is_queued_behind_it()
    {
        RecordingSender sender = new(OrderSendResult.Failed("offline", retryable: true));
        CashierService service = CreateService(sender);
        CashierCart cart = new();
        cart.Add(Ayran);

        CashierResult created = await service.SubmitAsync(Settings(), cart, new("Ada", "0555", "Kadıköy"),
            CashierPaymentOption.CashOnDelivery, isPickup: false, null, CancellationToken.None);
        CashierResult paid = await service.MarkPaidAsync(Settings(), created.Record!, "card", "SLIP-9",
            CancellationToken.None);

        Assert.True(created.Success);
        Assert.Equal(CashierOrderSyncState.Queued, created.Record!.SyncState);
        Assert.True(paid.Success);
        Assert.Single(sender.Sent); // the payment was not sent ahead of the queued order
        string[] files = Directory.GetFiles(Path.Combine(_directory, "inbox"), "*.json");
        Assert.Equal(2, files.Length);
        IReadOnlyList<CashierOrderRecord> journal = await new CashierOrderJournal(Path.Combine(_directory, "orders.json")).LoadAsync();
        Assert.True(Assert.Single(journal).IsPaid);
    }

    [Fact]
    public async Task Rejected_order_is_not_journaled_and_pickup_cannot_pay_at_door()
    {
        RecordingSender sender = new(OrderSendResult.Failed("bad request", retryable: false));
        CashierService service = CreateService(sender);
        CashierCart cart = new();
        cart.Add(Ayran);

        CashierResult rejected = await service.SubmitAsync(Settings(), cart, new("Ada", "0555", "Kadıköy"),
            CashierPaymentOption.PaidByCashAtCounter, false, null, CancellationToken.None);
        CashierResult pickupAtDoor = await service.SubmitAsync(Settings(), cart, new("Ada", "", ""),
            CashierPaymentOption.CashOnDelivery, isPickup: true, null, CancellationToken.None);

        Assert.False(rejected.Success);
        Assert.False(pickupAtDoor.Success);
        Assert.Empty(await new CashierOrderJournal(Path.Combine(_directory, "orders.json")).LoadAsync());
    }

    [Fact]
    public async Task Pickup_order_uses_store_address_and_allows_missing_phone()
    {
        RecordingSender sender = new(OrderSendResult.Sent(Guid.NewGuid(), false));
        CashierService service = CreateService(sender);
        CashierCart cart = new();
        cart.Add(Ayran);

        CashierResult result = await service.SubmitAsync(Settings(), cart, new("Ada", "", ""),
            CashierPaymentOption.PaidByCashAtCounter, isPickup: true, null, CancellationToken.None);

        Assert.True(result.Success);
        PosOrder sent = Assert.Single(sender.Sent);
        Assert.Equal(PosDeliveryFulfillment.CustomerPickup, sent.DeliveryFulfillment);
        Assert.Equal(CashierService.PickupAddress, sent.DeliveryAddress);
    }

    private CashierService CreateService(IOrderSender sender) =>
        new(sender, new CashierOrderJournal(Path.Combine(_directory, "orders.json")));

    private BridgeRuntimeSettings Settings() =>
        new("https://example.test/api/v1/webhooks/connection/orders", "secret", Path.Combine(_directory, "inbox"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class RecordingSender(OrderSendResult result) : IOrderSender
    {
        public List<PosOrder> Sent { get; } = [];

        public Task<OrderSendResult> SendAsync(BridgeRuntimeSettings settings, PosOrder order, CancellationToken cancellationToken)
        {
            Sent.Add(order);
            return Task.FromResult(result);
        }
    }
}

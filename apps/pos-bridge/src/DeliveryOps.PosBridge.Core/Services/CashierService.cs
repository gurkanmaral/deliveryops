using System.Security.Cryptography;
using System.Text.Json;
using DeliveryOps.PosBridge.Core.Abstractions;
using DeliveryOps.PosBridge.Core.Models;

namespace DeliveryOps.PosBridge.Core.Services;

public sealed record CashierResult(bool Success, string Message, CashierOrderRecord? Record = null)
{
    public static CashierResult Failed(string message) => new(false, message);
}

/// <summary>
/// Turns the counter screen into DeliveryOps orders and payment events. When the connection is down the
/// event is written to the bridge inbox instead, so the folder processor delivers it later and nothing is lost.
/// </summary>
public sealed class CashierService(IOrderSender sender, CashierOrderJournal journal, TimeProvider? timeProvider = null)
{
    public const string PickupAddress = "Mağazadan teslim alınacak";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<CashierResult> SubmitAsync(BridgeRuntimeSettings settings, CashierCart cart,
        CashierCustomer customer, CashierPaymentOption paymentOption, bool isPickup, string? paymentReference,
        CancellationToken cancellationToken)
    {
        if (cart.IsEmpty) return CashierResult.Failed("Sepet boş.");
        if (isPickup && paymentOption is CashierPaymentOption.CashOnDelivery or CashierPaymentOption.CardOnDelivery)
            return CashierResult.Failed("Gel-al siparişinde kapıda ödeme seçilemez.");
        string name = customer.Name.Trim();
        string phone = customer.Phone.Trim();
        string address = isPickup ? PickupAddress : customer.Address.Trim();
        if (string.IsNullOrWhiteSpace(name)) return CashierResult.Failed("Müşteri adı zorunludur.");
        if (string.IsNullOrWhiteSpace(phone))
        {
            if (!isPickup) return CashierResult.Failed("Paket servis için telefon zorunludur.");
            phone = "-";
        }
        if (string.IsNullOrWhiteSpace(address)) return CashierResult.Failed("Teslimat adresi zorunludur.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string externalOrderId = $"KASA-{now:yyMMddHHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2))}";
        decimal total = cart.Total;
        PosPayment payment = BuildPayment(paymentOption, total, paymentReference, now);
        PosOrder order = new(externalOrderId, name, phone, address, total, externalOrderId, "order.created",
            BuildInstructions(cart, customer.Note),
            isPickup ? PosDeliveryFulfillment.CustomerPickup : PosDeliveryFulfillment.MerchantCourier, payment);
        IReadOnlyList<string> errors = order.Validate();
        if (errors.Count > 0) return CashierResult.Failed(string.Join(" ", errors));

        (bool delivered, string? error) = await DeliverAsync(settings, order, alwaysQueue: false, cancellationToken);
        if (error is not null) return CashierResult.Failed(error);
        CashierOrderRecord record = new(externalOrderId, now, name, total, cart.Summary(), payment.Method,
            payment.Status == "paid", payment.Reference,
            delivered ? CashierOrderSyncState.Sent : CashierOrderSyncState.Queued, order);
        await journal.UpsertAsync(record, cancellationToken);
        return new CashierResult(true, delivered
            ? $"{externalOrderId} gönderildi."
            : $"{externalOrderId} bağlantı gelince gönderilecek (kuyruğa alındı).", record);
    }

    /// <summary>Reports a payment taken after the order was sent, e.g. on the NarPOS device at the counter.</summary>
    public async Task<CashierResult> MarkPaidAsync(BridgeRuntimeSettings settings, CashierOrderRecord record,
        string method, string? reference, CancellationToken cancellationToken)
    {
        if (record.IsPaid) return new CashierResult(true, "Sipariş zaten ödenmiş.", record);
        if (method is not ("cash" or "card")) return CashierResult.Failed("Ödeme yöntemi nakit veya kart olmalıdır.");
        DateTimeOffset now = _timeProvider.GetUtcNow();
        PosPayment payment = new(method, "paid", record.TotalAmount, NormalizeReference(reference), now);
        PosOrder paidEvent = record.Order with
        {
            EventId = $"{record.ExternalOrderId}:paid",
            EventType = "order.paid",
            Payment = payment
        };
        // If the order itself is still waiting in the inbox, queue the payment behind it so it arrives second.
        (bool delivered, string? error) = await DeliverAsync(settings, paidEvent,
            alwaysQueue: record.SyncState == CashierOrderSyncState.Queued, cancellationToken);
        if (error is not null) return CashierResult.Failed(error);
        CashierOrderRecord updated = record with
        {
            IsPaid = true,
            PaymentMethod = method,
            PaymentReference = payment.Reference,
            Order = record.Order with { Payment = payment }
        };
        await journal.UpsertAsync(updated, cancellationToken);
        return new CashierResult(true, delivered
            ? "Ödeme panele iletildi."
            : "Ödeme kaydedildi; bağlantı gelince panele iletilecek.", updated);
    }

    private async Task<(bool Delivered, string? Error)> DeliverAsync(BridgeRuntimeSettings settings,
        PosOrder order, bool alwaysQueue, CancellationToken cancellationToken)
    {
        if (!alwaysQueue)
        {
            OrderSendResult result = await sender.SendAsync(settings, order, cancellationToken);
            if (result.Success) return (true, null);
            if (!result.Retryable) return (false, result.Error ?? "Sipariş reddedildi.");
        }
        await EnqueueAsync(settings.InboxDirectory, order, cancellationToken);
        return (false, null);
    }

    private static async Task EnqueueAsync(string inboxDirectory, PosOrder order, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(inboxDirectory);
        string fileName = $"{(order.EventId ?? order.ExternalOrderId).Replace(':', '-')}.json";
        string path = Path.Combine(inboxDirectory, fileName);
        string temporaryPath = path + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, order, JsonOptions, cancellationToken);
        File.Move(temporaryPath, path, true);
    }

    private static PosPayment BuildPayment(CashierPaymentOption option, decimal total, string? reference,
        DateTimeOffset now) => option switch
    {
        CashierPaymentOption.PaidByCardAtCounter => new("card", "paid", total, NormalizeReference(reference), now),
        CashierPaymentOption.PaidByCashAtCounter => new("cash", "paid", total, NormalizeReference(reference), now),
        CashierPaymentOption.CashOnDelivery => new("cash", "unpaid"),
        CashierPaymentOption.CardOnDelivery => new("card", "unpaid"),
        CashierPaymentOption.PaidOnline => new("online", "paid", total, NormalizeReference(reference), now),
        _ => throw new ArgumentOutOfRangeException(nameof(option))
    };

    private static string BuildInstructions(CashierCart cart, string? note)
    {
        string text = $"Sipariş: {cart.Summary()}";
        if (!string.IsNullOrWhiteSpace(note)) text += $" • Not: {note.Trim()}";
        return text.Length <= 2000 ? text : text[..2000];
    }

    private static string? NormalizeReference(string? reference) =>
        string.IsNullOrWhiteSpace(reference) ? null : reference.Trim()[..Math.Min(reference.Trim().Length, 100)];
}

/// <summary>
/// Used until a NarPOS integration is configured: the cashier takes the payment on the NarPOS device and
/// confirms it in the app (optionally entering the slip/authorization number).
/// </summary>
public sealed class ManualPaymentTerminal : IPaymentTerminal
{
    public string Name => "NarPOS (manuel onay)";
    public bool ReportsResultAutomatically => false;

    public Task<PaymentTerminalResult> ChargeAsync(decimal amount, string orderReference,
        CancellationToken cancellationToken) =>
        Task.FromResult(PaymentTerminalResult.Declined("Bu terminal sonucu otomatik bildirmiyor; ödemeyi kasiyer onaylamalıdır."));
}

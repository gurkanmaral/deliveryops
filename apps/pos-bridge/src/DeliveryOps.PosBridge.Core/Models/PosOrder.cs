namespace DeliveryOps.PosBridge.Core.Models;

/// <summary>
/// The canonical-v1 webhook body. Files dropped by other POS software only need the first five fields;
/// the cashier screen also sends the event id/type, fulfillment and payment.
/// </summary>
public sealed record PosOrder(
    string ExternalOrderId,
    string CustomerName,
    string CustomerPhone,
    string DeliveryAddress,
    decimal TotalAmount,
    string? EventId = null,
    string? EventType = null,
    string? DeliveryInstructions = null,
    PosDeliveryFulfillment DeliveryFulfillment = PosDeliveryFulfillment.MerchantCourier,
    PosPayment? Payment = null)
{
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(ExternalOrderId)) errors.Add("externalOrderId is required.");
        else if (ExternalOrderId.Length > 120) errors.Add("externalOrderId cannot exceed 120 characters.");
        if (string.IsNullOrWhiteSpace(CustomerName)) errors.Add("customerName is required.");
        else if (CustomerName.Length > 200) errors.Add("customerName cannot exceed 200 characters.");
        if (string.IsNullOrWhiteSpace(CustomerPhone)) errors.Add("customerPhone is required.");
        else if (CustomerPhone.Length > 50) errors.Add("customerPhone cannot exceed 50 characters.");
        if (string.IsNullOrWhiteSpace(DeliveryAddress)) errors.Add("deliveryAddress is required.");
        else if (DeliveryAddress.Length > 1000) errors.Add("deliveryAddress cannot exceed 1000 characters.");
        if (TotalAmount < 0) errors.Add("totalAmount cannot be negative.");
        if (TotalAmount > 100_000_000) errors.Add("totalAmount exceeds the supported limit.");
        if (DeliveryInstructions?.Length > 2000) errors.Add("deliveryInstructions cannot exceed 2000 characters.");
        if (Payment is not null)
        {
            if (Payment.Method is not ("cash" or "card" or "online")) errors.Add("payment.method must be cash, card or online.");
            if (Payment.Status is not ("paid" or "unpaid")) errors.Add("payment.status must be paid or unpaid.");
            if (Payment.Amount is < 0) errors.Add("payment.amount cannot be negative.");
            if (Payment.Reference?.Length > 100) errors.Add("payment.reference cannot exceed 100 characters.");
        }
        if (string.Equals(EventType, "order.paid", StringComparison.OrdinalIgnoreCase) && Payment?.Status != "paid")
            errors.Add("order.paid events must carry a paid payment.");
        return errors;
    }
}

/// <summary>Numeric values match the Integrations API's delivery fulfillment enum.</summary>
public enum PosDeliveryFulfillment { MerchantCourier = 0, ProviderCourier = 1, CustomerPickup = 2 }

/// <summary>Method is "cash", "card" or "online"; status is "paid" or "unpaid".</summary>
public sealed record PosPayment(string Method, string Status, decimal? Amount = null, string? Reference = null,
    DateTimeOffset? PaidAtUtc = null);

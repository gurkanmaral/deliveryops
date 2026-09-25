namespace DeliveryOps.PosBridge.Core.Models;

public sealed record PosOrder(
    string ExternalOrderId,
    string CustomerName,
    string CustomerPhone,
    string DeliveryAddress,
    decimal TotalAmount)
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
        return errors;
    }
}

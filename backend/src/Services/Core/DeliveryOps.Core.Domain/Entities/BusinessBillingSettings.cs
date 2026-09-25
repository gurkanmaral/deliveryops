using DeliveryOps.BuildingBlocks.Domain;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class BusinessBillingSettings : Entity
{
    private BusinessBillingSettings() { }
    private BusinessBillingSettings(Guid businessId) => BusinessId = businessId;

    public Guid BusinessId { get; private init; }
    public decimal FeePerDeliveredOrder { get; private set; }
    public decimal CommissionRatePercent { get; private set; }
    public decimal FeePerReturnedOrder { get; private set; }
    public decimal TaxRatePercent { get; private set; } = 20m;
    public string Currency { get; private set; } = "TRY";

    public static BusinessBillingSettings CreateDefault(Guid businessId)
    {
        if (businessId == Guid.Empty) throw new ArgumentException("Business is required.", nameof(businessId));
        return new BusinessBillingSettings(businessId);
    }

    public void Update(decimal feePerDeliveredOrder, decimal commissionRatePercent,
        decimal feePerReturnedOrder, decimal taxRatePercent)
    {
        if (feePerDeliveredOrder is < 0 or > 100_000) throw new ArgumentOutOfRangeException(nameof(feePerDeliveredOrder));
        if (feePerReturnedOrder is < 0 or > 100_000) throw new ArgumentOutOfRangeException(nameof(feePerReturnedOrder));
        if (commissionRatePercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(commissionRatePercent));
        if (taxRatePercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(taxRatePercent));
        FeePerDeliveredOrder = decimal.Round(feePerDeliveredOrder, 2);
        CommissionRatePercent = decimal.Round(commissionRatePercent, 4);
        FeePerReturnedOrder = decimal.Round(feePerReturnedOrder, 2);
        TaxRatePercent = decimal.Round(taxRatePercent, 2);
        MarkAsUpdated();
    }
}

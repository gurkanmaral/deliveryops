using DeliveryOps.BuildingBlocks.Domain;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class BillingSettlement : Entity
{
    private BillingSettlement() { }

    public Guid BusinessId { get; private init; }
    public DateOnly PeriodFrom { get; private init; }
    public DateOnly PeriodTo { get; private init; }
    public BillingSettlementStatus Status { get; private set; }
    public int DeliveredOrderCount { get; private set; }
    public int ReturnedOrderCount { get; private set; }
    public int CancelledOrderCount { get; private set; }
    public decimal DeliveredOrderValue { get; private set; }
    public decimal FeePerDeliveredOrder { get; private set; }
    public decimal CommissionRatePercent { get; private set; }
    public decimal FeePerReturnedOrder { get; private set; }
    public decimal TaxRatePercent { get; private set; }
    public decimal DeliveryFeeAmount { get; private set; }
    public decimal CommissionAmount { get; private set; }
    public decimal ReturnFeeAmount { get; private set; }
    public decimal SubtotalAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private init; } = "TRY";
    public Guid CreatedByUserId { get; private init; }
    public Guid? FinalizedByUserId { get; private set; }
    public DateTimeOffset? FinalizedAtUtc { get; private set; }
    public string DocumentNumber { get; private set; } = string.Empty;

    public static BillingSettlement Create(Guid businessId, DateOnly periodFrom, DateOnly periodTo,
        int deliveredOrderCount, int returnedOrderCount, int cancelledOrderCount,
        decimal deliveredOrderValue, BusinessBillingSettings settings, Guid createdByUserId)
    {
        if (businessId == Guid.Empty) throw new ArgumentException("Business is required.", nameof(businessId));
        if (periodTo < periodFrom) throw new ArgumentException("Period is invalid.", nameof(periodTo));
        BillingSettlement settlement = new()
        {
            BusinessId = businessId,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            CreatedByUserId = createdByUserId
        };
        settlement.Recalculate(deliveredOrderCount, returnedOrderCount, cancelledOrderCount,
            deliveredOrderValue, settings);
        return settlement;
    }

    public void Recalculate(int deliveredOrderCount, int returnedOrderCount, int cancelledOrderCount,
        decimal deliveredOrderValue, BusinessBillingSettings settings)
    {
        if (Status == BillingSettlementStatus.Finalized)
            throw new InvalidOperationException("Kesinleşmiş mutabakat yeniden hesaplanamaz.");
        if (deliveredOrderCount < 0 || returnedOrderCount < 0 || cancelledOrderCount < 0 || deliveredOrderValue < 0)
            throw new ArgumentOutOfRangeException(nameof(deliveredOrderCount));
        DeliveredOrderCount = deliveredOrderCount;
        ReturnedOrderCount = returnedOrderCount;
        CancelledOrderCount = cancelledOrderCount;
        DeliveredOrderValue = decimal.Round(deliveredOrderValue, 2);
        FeePerDeliveredOrder = settings.FeePerDeliveredOrder;
        CommissionRatePercent = settings.CommissionRatePercent;
        FeePerReturnedOrder = settings.FeePerReturnedOrder;
        TaxRatePercent = settings.TaxRatePercent;
        DeliveryFeeAmount = decimal.Round(deliveredOrderCount * FeePerDeliveredOrder, 2);
        CommissionAmount = decimal.Round(DeliveredOrderValue * CommissionRatePercent / 100m, 2);
        ReturnFeeAmount = decimal.Round(returnedOrderCount * FeePerReturnedOrder, 2);
        SubtotalAmount = DeliveryFeeAmount + CommissionAmount + ReturnFeeAmount;
        TaxAmount = decimal.Round(SubtotalAmount * TaxRatePercent / 100m, 2);
        TotalAmount = SubtotalAmount + TaxAmount;
        MarkAsUpdated();
    }

    public void Finalize(Guid userId, DateTimeOffset now)
    {
        if (Status == BillingSettlementStatus.Finalized) return;
        Status = BillingSettlementStatus.Finalized;
        DocumentNumber = $"MUT-{PeriodTo:yyyyMM}-{Id.ToString("N")[..8].ToUpperInvariant()}";
        FinalizedByUserId = userId;
        FinalizedAtUtc = now;
        MarkAsUpdated();
    }
}

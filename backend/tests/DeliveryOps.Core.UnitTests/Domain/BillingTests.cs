using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using Xunit;

namespace DeliveryOps.Core.UnitTests.Domain;

public sealed class BillingTests
{
    [Fact]
    public void Settlement_calculates_fee_commission_and_tax_snapshot()
    {
        BusinessBillingSettings settings = BusinessBillingSettings.CreateDefault(Guid.NewGuid());
        settings.Update(12.50m, 2.5m, 6m, 20m);

        BillingSettlement settlement = BillingSettlement.Create(settings.BusinessId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 10, 2, 1,
            2_000m, settings, Guid.NewGuid());

        Assert.Equal(125m, settlement.DeliveryFeeAmount);
        Assert.Equal(50m, settlement.CommissionAmount);
        Assert.Equal(12m, settlement.ReturnFeeAmount);
        Assert.Equal(187m, settlement.SubtotalAmount);
        Assert.Equal(37.40m, settlement.TaxAmount);
        Assert.Equal(224.40m, settlement.TotalAmount);
    }

    [Fact]
    public void Finalized_settlement_cannot_be_recalculated()
    {
        BusinessBillingSettings settings = BusinessBillingSettings.CreateDefault(Guid.NewGuid());
        BillingSettlement settlement = BillingSettlement.Create(settings.BusinessId,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 0, 0, 0,
            0, settings, Guid.NewGuid());
        settlement.Finalize(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(BillingSettlementStatus.Finalized, settlement.Status);
        Assert.Throws<InvalidOperationException>(() => settlement.Recalculate(1, 0, 0, 100, settings));
    }

    [Theory]
    [InlineData(-1, 0, 0, 20)]
    [InlineData(0, 101, 0, 20)]
    [InlineData(0, 0, -1, 20)]
    [InlineData(0, 0, 0, 101)]
    public void Invalid_billing_settings_are_rejected(decimal deliveredFee, decimal commission,
        decimal returnedFee, decimal tax)
    {
        BusinessBillingSettings settings = BusinessBillingSettings.CreateDefault(Guid.NewGuid());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            settings.Update(deliveredFee, commission, returnedFee, tax));
    }
}

using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

internal sealed class BillingSettlementConfiguration : IEntityTypeConfiguration<BillingSettlement>
{
    public void Configure(EntityTypeBuilder<BillingSettlement> builder)
    {
        builder.ToTable("billing_settlements");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.BusinessId, x.PeriodFrom, x.PeriodTo }).IsUnique();
        builder.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.DeliveredOrderValue).HasPrecision(18, 2);
        builder.Property(x => x.FeePerDeliveredOrder).HasPrecision(18, 2);
        builder.Property(x => x.CommissionRatePercent).HasPrecision(9, 4);
        builder.Property(x => x.FeePerReturnedOrder).HasPrecision(18, 2);
        builder.Property(x => x.TaxRatePercent).HasPrecision(9, 2);
        builder.Property(x => x.DeliveryFeeAmount).HasPrecision(18, 2);
        builder.Property(x => x.CommissionAmount).HasPrecision(18, 2);
        builder.Property(x => x.ReturnFeeAmount).HasPrecision(18, 2);
        builder.Property(x => x.SubtotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.TaxAmount).HasPrecision(18, 2);
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3);
        builder.Property(x => x.DocumentNumber).HasMaxLength(40);
        builder.HasIndex(x => x.DocumentNumber).IsUnique().HasFilter("\"DocumentNumber\" <> ''");
    }
}

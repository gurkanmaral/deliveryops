using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

internal sealed class BusinessBillingSettingsConfiguration : IEntityTypeConfiguration<BusinessBillingSettings>
{
    public void Configure(EntityTypeBuilder<BusinessBillingSettings> builder)
    {
        builder.ToTable("business_billing_settings");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.BusinessId).IsUnique();
        builder.HasOne<Business>().WithOne().HasForeignKey<BusinessBillingSettings>(x => x.BusinessId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.FeePerDeliveredOrder).HasPrecision(18, 2);
        builder.Property(x => x.CommissionRatePercent).HasPrecision(9, 4);
        builder.Property(x => x.FeePerReturnedOrder).HasPrecision(18, 2);
        builder.Property(x => x.TaxRatePercent).HasPrecision(9, 2);
        builder.Property(x => x.Currency).HasMaxLength(3);
    }
}

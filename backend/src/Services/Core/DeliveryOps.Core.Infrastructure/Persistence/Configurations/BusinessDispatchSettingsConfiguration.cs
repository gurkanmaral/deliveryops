using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

internal sealed class BusinessDispatchSettingsConfiguration : IEntityTypeConfiguration<BusinessDispatchSettings>
{
    public void Configure(EntityTypeBuilder<BusinessDispatchSettings> builder)
    {
        builder.ToTable("business_dispatch_settings");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.BusinessId).IsUnique();
        builder.Property(x => x.PreferDeliveryClusters).HasDefaultValue(true);
        builder.Property(x => x.DeliveryClusterRadiusKm).HasDefaultValue(2d);
        builder.Property(x => x.DeliveryClusterMaxBearingDegrees).HasDefaultValue(45d);
        builder.HasOne<Business>().WithOne().HasForeignKey<BusinessDispatchSettings>(x => x.BusinessId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

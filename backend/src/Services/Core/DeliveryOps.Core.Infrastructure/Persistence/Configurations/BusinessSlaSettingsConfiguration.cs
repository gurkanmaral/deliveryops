using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

internal sealed class BusinessSlaSettingsConfiguration : IEntityTypeConfiguration<BusinessSlaSettings>
{
    public void Configure(EntityTypeBuilder<BusinessSlaSettings> builder)
    {
        builder.ToTable("business_sla_settings");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.BusinessId).IsUnique();
        builder.HasOne<Business>().WithOne().HasForeignKey<BusinessSlaSettings>(x => x.BusinessId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

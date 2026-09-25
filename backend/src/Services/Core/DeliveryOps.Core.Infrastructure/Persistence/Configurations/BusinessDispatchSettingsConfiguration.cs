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
        builder.HasOne<Business>().WithOne().HasForeignKey<BusinessDispatchSettings>(x => x.BusinessId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

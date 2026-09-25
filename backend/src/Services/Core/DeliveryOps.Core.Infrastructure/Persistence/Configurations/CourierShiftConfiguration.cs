using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

public sealed class CourierShiftConfiguration : IEntityTypeConfiguration<CourierShift>
{
    public void Configure(EntityTypeBuilder<CourierShift> builder)
    {
        builder.ToTable("courier_shifts");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CourierId, x.StartedAtUtc });
        builder.HasIndex(x => x.CourierId).HasFilter("\"EndedAtUtc\" IS NULL").IsUnique();
    }
}

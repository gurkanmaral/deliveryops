using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

public sealed class CourierLocationConfiguration : IEntityTypeConfiguration<CourierLocation>
{
    public void Configure(EntityTypeBuilder<CourierLocation> builder)
    {
        builder.ToTable("courier_locations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Position).HasColumnType("geography (point, 4326)").IsRequired();
        builder.HasIndex(x => x.Position).HasMethod("gist");
        builder.HasIndex(x => new { x.CourierId, x.RecordedAtUtc });
        builder.HasIndex(x => new { x.BusinessId, x.RecordedAtUtc });
    }
}

using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExternalId).HasMaxLength(100);
        builder.Property(x => x.CustomerName).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CustomerPhone).HasMaxLength(500).IsRequired();
        builder.Property(x => x.DeliveryAddress).HasMaxLength(3000).IsRequired();
        builder.Property(x => x.DeliveryInstructions).HasMaxLength(2000);
        builder.Property(x => x.CustomerSearchTokens).HasColumnType("text").IsRequired();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CancellationReason).HasMaxLength(500);
        builder.Property(x => x.DeliveryFailureReason).HasMaxLength(500);
        builder.Property(x => x.CreationIdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreationRequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Version).IsRowVersion();
        builder.Ignore(x => x.AllowedNextStatuses);
        builder.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.BusinessId, x.BranchId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.BusinessId, x.CourierId, x.Status });
        builder.HasIndex(x => new { x.BusinessId, x.Status });
        builder.HasIndex(x => new { x.BusinessId, x.DeliveryLatitude, x.DeliveryLongitude });
        builder.HasIndex(x => new { x.BusinessId, x.Source, x.ExternalId }).IsUnique()
            .HasFilter("\"ExternalId\" <> ''");
        builder.HasIndex(x => new { x.BusinessId, x.CreationIdempotencyKey }).IsUnique()
            .HasFilter("\"CreationIdempotencyKey\" <> ''");
        builder.HasMany(x => x.StatusHistory)
            .WithOne(x => x.Order)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

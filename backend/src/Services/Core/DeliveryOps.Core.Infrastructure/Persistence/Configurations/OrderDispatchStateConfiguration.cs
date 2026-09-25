using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

internal sealed class OrderDispatchStateConfiguration : IEntityTypeConfiguration<OrderDispatchState>
{
    public void Configure(EntityTypeBuilder<OrderDispatchState> builder)
    {
        builder.ToTable("order_dispatch_states");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LastReason).HasMaxLength(500);
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => new { x.BusinessId, x.Status, x.NextAttemptAtUtc });
        builder.HasOne<Order>().WithOne().HasForeignKey<OrderDispatchState>(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

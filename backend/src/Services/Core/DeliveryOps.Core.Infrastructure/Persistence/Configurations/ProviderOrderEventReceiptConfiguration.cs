using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

internal sealed class ProviderOrderEventReceiptConfiguration : IEntityTypeConfiguration<ProviderOrderEventReceipt>
{
    public void Configure(EntityTypeBuilder<ProviderOrderEventReceipt> builder)
    {
        builder.ToTable("provider_order_event_receipts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasConversion<int>();
        builder.Property(x => x.ExternalEventId).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ProviderStatus).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => new { x.BusinessId, x.Source, x.ExternalEventId }).IsUnique();
        builder.HasIndex(x => x.OrderId);
        builder.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

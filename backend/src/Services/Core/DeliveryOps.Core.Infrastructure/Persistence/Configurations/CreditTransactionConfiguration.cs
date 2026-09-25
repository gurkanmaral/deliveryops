using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

internal sealed class CreditTransactionConfiguration : IEntityTypeConfiguration<CreditTransaction>
{
    public void Configure(EntityTypeBuilder<CreditTransaction> builder)
    {
        builder.ToTable("credit_transactions");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
        builder.HasIndex(x => x.OrderId).HasDatabaseName("IX_credit_transactions_RefundOrderId")
            .IsUnique().HasFilter("\"OrderId\" IS NOT NULL AND \"Type\" = 3");
        builder.HasIndex(x => new { x.BusinessId, x.IdempotencyKey }).IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Description).HasMaxLength(300);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

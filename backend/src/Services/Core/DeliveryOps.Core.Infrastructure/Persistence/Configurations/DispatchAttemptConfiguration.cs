using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

internal sealed class DispatchAttemptConfiguration : IEntityTypeConfiguration<DispatchAttempt>
{
    public void Configure(EntityTypeBuilder<DispatchAttempt> builder)
    {
        builder.ToTable("dispatch_attempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Trigger).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.HasIndex(x => new { x.OrderId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

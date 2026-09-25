using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

internal sealed class OperationalAlertConfiguration : IEntityTypeConfiguration<OperationalAlert>
{
    public void Configure(EntityTypeBuilder<OperationalAlert> builder)
    {
        builder.ToTable("operational_alerts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AlertKey).HasMaxLength(180).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(180).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => x.AlertKey).IsUnique();
        builder.HasIndex(x => new { x.BusinessId, x.Status, x.Severity });
        builder.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Courier>().WithMany().HasForeignKey(x => x.CourierId).OnDelete(DeleteBehavior.Cascade);
    }
}

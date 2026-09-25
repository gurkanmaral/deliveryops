using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeliveryOps.Core.Infrastructure.Persistence.Configurations;

internal sealed class IntegrationOutboxMessageConfiguration : IEntityTypeConfiguration<IntegrationOutboxMessage>
{
    public void Configure(EntityTypeBuilder<IntegrationOutboxMessage> builder)
    {
        builder.ToTable("integration_outbox");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.HasIndex(x => new { x.ProcessedAtUtc, x.DeadLetteredAtUtc, x.NextAttemptAtUtc })
            .HasFilter("\"ProcessedAtUtc\" IS NULL AND \"DeadLetteredAtUtc\" IS NULL");
    }
}

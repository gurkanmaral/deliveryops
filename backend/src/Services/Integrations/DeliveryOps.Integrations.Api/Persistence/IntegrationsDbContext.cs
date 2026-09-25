using DeliveryOps.Integrations.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Integrations.Api.Persistence;

public sealed class IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options) : DbContext(options)
{
    public DbSet<IntegrationConnection> Connections => Set<IntegrationConnection>();
    public DbSet<InboundOrderEvent> InboundOrderEvents => Set<InboundOrderEvent>();
    public DbSet<OutboundOrderEvent> OutboundOrderEvents => Set<OutboundOrderEvent>();
    public DbSet<IntegrationHealthCheck> HealthChecks => Set<IntegrationHealthCheck>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("integrations");
        builder.Entity<IntegrationConnection>(entity =>
        {
            entity.ToTable("connections");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(120).IsRequired();
            entity.Property(item => item.SecretHash).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ProtectedSecret).HasMaxLength(1000);
            entity.Property(item => item.PreviousSecretHash).HasMaxLength(64);
            entity.Property(item => item.PreviousProtectedSecret).HasMaxLength(1000);
            entity.Property(item => item.AuthMode).HasConversion<int>();
            entity.Property(item => item.AdapterVersion).HasMaxLength(60).IsRequired();
            entity.Property(item => item.ProtectedCredentials).HasMaxLength(4000);
            entity.Property(item => item.ProviderAccountId).HasMaxLength(120);
            entity.Property(item => item.ProviderEnvironment).HasConversion<int>();
            entity.Property(item => item.LastHealthCheckMessage).HasMaxLength(500);
            entity.HasIndex(item => new { item.BusinessId, item.BranchId, item.Provider });
        });
        builder.Entity<InboundOrderEvent>(entity =>
        {
            entity.ToTable("inbound_order_events");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ExternalEventId).HasMaxLength(120).IsRequired();
            entity.Property(item => item.ExternalOrderId).HasMaxLength(120).IsRequired();
            entity.Property(item => item.EventType).HasMaxLength(80).IsRequired();
            entity.Property(item => item.AdapterVersion).HasMaxLength(60).IsRequired();
            entity.Property(item => item.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(item => item.RawPayload).HasColumnType("jsonb").IsRequired();
            entity.Property(item => item.NormalizedPayload).HasColumnType("jsonb").IsRequired();
            entity.Property(item => item.LastError).HasMaxLength(1000);
            entity.HasIndex(item => new { item.ConnectionId, item.ExternalEventId }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.ReceivedAtUtc });
            entity.HasIndex(item => new { item.Status, item.NextAttemptAtUtc });
            entity.HasOne(item => item.Connection).WithMany().HasForeignKey(item => item.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<OutboundOrderEvent>(entity =>
        {
            entity.ToTable("outbound_order_events");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ExternalOrderId).HasMaxLength(120).IsRequired();
            entity.Property(item => item.ProviderStatus).HasMaxLength(60).IsRequired();
            entity.Property(item => item.CancellationReason).HasMaxLength(500);
            entity.Property(item => item.LastError).HasMaxLength(1000);
            entity.HasIndex(item => item.SourceEventId).IsUnique();
            entity.HasIndex(item => new { item.ConnectionId, item.ExternalOrderId, item.ProviderStatus }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.NextAttemptAtUtc });
            entity.HasOne(item => item.Connection).WithMany().HasForeignKey(item => item.ConnectionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<IntegrationHealthCheck>(entity =>
        {
            entity.ToTable("health_checks");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Message).HasMaxLength(500).IsRequired();
            entity.Property(item => item.Environment).HasConversion<int>();
            entity.HasIndex(item => new { item.ConnectionId, item.CheckedAtUtc });
            entity.HasOne(item => item.Connection).WithMany().HasForeignKey(item => item.ConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

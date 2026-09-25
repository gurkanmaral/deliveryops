using DeliveryOps.Notifications.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Notifications.Api.Persistence;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<PushDevice> PushDevices => Set<PushDevice>();
    public DbSet<NotificationReceipt> NotificationReceipts => Set<NotificationReceipt>();
    public DbSet<ExpoPushReceipt> ExpoPushReceipts => Set<ExpoPushReceipt>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("notifications");
        builder.Entity<PushDevice>(entity =>
        {
            entity.ToTable("push_devices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExpoPushToken).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Platform).HasMaxLength(20).IsRequired();
            entity.Property(x => x.DeviceName).HasMaxLength(120);
            entity.HasIndex(x => x.ExpoPushToken).IsUnique();
            entity.HasIndex(x => new { x.BusinessId, x.BranchId, x.IsActive });
            entity.HasIndex(x => new { x.CourierId, x.IsActive });
        });
        builder.Entity<NotificationReceipt>(entity =>
        {
            entity.ToTable("notification_receipts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => x.EventId).IsUnique();
        });
        builder.Entity<ExpoPushReceipt>(entity =>
        {
            entity.ToTable("expo_push_receipts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TicketId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ExpoPushToken).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Error).HasMaxLength(100);
            entity.HasIndex(x => x.TicketId).IsUnique();
            entity.HasIndex(x => new { x.CompletedAtUtc, x.NextCheckAtUtc });
        });
    }
}

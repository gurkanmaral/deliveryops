using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Queries.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;
using NetTopologySuite.Geometries;
using DeliveryOps.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DeliveryOps.Core.Infrastructure.Persistence;

public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options, IRequestContext requestContext,
    IOrderPiiProtector orderPiiProtector)
    : DbContext(options), ICoreDbContext
{
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Courier> Couriers => Set<Courier>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CourierLocation> CourierLocations => Set<CourierLocation>();
    public DbSet<CourierShift> CourierShifts => Set<CourierShift>();
    public DbSet<NotificationOutboxMessage> NotificationOutbox => Set<NotificationOutboxMessage>();
    public DbSet<IntegrationOutboxMessage> IntegrationOutbox => Set<IntegrationOutboxMessage>();
    public DbSet<ProviderOrderEventReceipt> ProviderOrderEventReceipts => Set<ProviderOrderEventReceipt>();
    public DbSet<BusinessDispatchSettings> BusinessDispatchSettings => Set<BusinessDispatchSettings>();
    public DbSet<OrderDispatchState> OrderDispatchStates => Set<OrderDispatchState>();
    public DbSet<DispatchAttempt> DispatchAttempts => Set<DispatchAttempt>();
    public DbSet<BusinessSlaSettings> BusinessSlaSettings => Set<BusinessSlaSettings>();
    public DbSet<OperationalAlert> OperationalAlerts => Set<OperationalAlert>();
    public DbSet<BusinessBillingSettings> BusinessBillingSettings => Set<BusinessBillingSettings>();
    public DbSet<BillingSettlement> BillingSettlements => Set<BillingSettlement>();
    public DbSet<BusinessCreditAccount> BusinessCreditAccounts => Set<BusinessCreditAccount>();
    public DbSet<CreditTransaction> CreditTransactions => Set<CreditTransaction>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        RefreshOrderPiiSearchTokens();
        AddNotificationOutboxMessages();
        AddIntegrationOutboxMessages();
        AddAuditLogs();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SavePiiMaintenanceChangesAsync(CancellationToken cancellationToken = default)
    {
        RefreshOrderPiiSearchTokens();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void RefreshOrderPiiSearchTokens()
    {
        ChangeTracker.DetectChanges();
        foreach (EntityEntry<Order> entry in ChangeTracker.Entries<Order>().Where(entry =>
                     entry.State == EntityState.Added ||
                     entry.State == EntityState.Modified &&
                     (entry.Property(nameof(Order.CustomerName)).IsModified || entry.Property(nameof(Order.CustomerPhone)).IsModified)))
        {
            string tokens = entry.Entity.PiiAnonymizedAtUtc.HasValue
                ? string.Empty
                : orderPiiProtector.BuildSearchTokens(entry.Entity.CustomerName, entry.Entity.CustomerPhone);
            entry.Entity.SetCustomerSearchTokens(tokens);
        }
    }

    private void AddIntegrationOutboxMessages()
    {
        ChangeTracker.DetectChanges();
        List<EntityEntry<Order>> orders = ChangeTracker.Entries<Order>().Where(x =>
            x.State is EntityState.Added or EntityState.Modified &&
            (x.State == EntityState.Added || x.Property(nameof(Order.Status)).IsModified) &&
            x.Entity.Source == OrderSource.Yemeksepeti &&
            x.Entity.Status is OrderStatus.WaitingForCourier or OrderStatus.PickedUp or OrderStatus.Cancelled).ToList();
        foreach (EntityEntry<Order> entry in orders)
            IntegrationOutbox.Add(IntegrationOutboxMessage.CreateForOrder(entry.Entity));
    }

    private void AddNotificationOutboxMessages()
    {
        ChangeTracker.DetectChanges();
        List<EntityEntry<Order>> ordersToNotify = ChangeTracker.Entries<Order>().Where(x =>
                     x.State is EntityState.Added or EntityState.Modified &&
                     (x.State == EntityState.Added || x.Property(nameof(Order.Status)).IsModified) &&
                     x.Entity.DeliveryFulfillment == DeliveryFulfillmentType.MerchantCourier &&
                     x.Entity.Status is OrderStatus.WaitingForCourier or OrderStatus.Assigned).ToList();
        foreach (EntityEntry<Order> entry in ordersToNotify)
        {
            NotificationOutbox.Add(NotificationOutboxMessage.CreateForOrder(entry.Entity));
        }
    }

    private void AddAuditLogs()
    {
        ChangeTracker.DetectChanges();
        List<AuditLog> logs = [];
        foreach (EntityEntry entry in ChangeTracker.Entries().Where(x => x.Entity is not (AuditLog or NotificationOutboxMessage or IntegrationOutboxMessage or ProviderOrderEventReceipt or DispatchAttempt or OperationalAlert or CourierLocation) && x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (IsTelemetryOnlyChange(entry)) continue;
            Dictionary<string, object?> changes = entry.Properties
                .Where(property => entry.State != EntityState.Modified || property.IsModified)
                .ToDictionary(
                    property => property.Metadata.Name,
                    property => entry.State == EntityState.Modified
                        ? new { before = AuditValue(property.Metadata.Name, property.OriginalValue), after = AuditValue(property.Metadata.Name, property.CurrentValue) }
                        : AuditValue(property.Metadata.Name, entry.State == EntityState.Deleted ? property.OriginalValue : property.CurrentValue));
            Guid userId;
            try { userId = requestContext.UserId; } catch { userId = Guid.Empty; }
            Guid? businessId = TryGuid(entry, "BusinessId") ?? (entry.Entity is Business business ? business.Id : TryRequestBusinessId());
            logs.Add(AuditLog.Create(userId, businessId, entry.State.ToString(), entry.Metadata.ClrType.Name,
                entry.Property("Id").CurrentValue?.ToString() ?? string.Empty, JsonSerializer.Serialize(changes)));
        }
        AuditLogs.AddRange(logs);
    }

    private static readonly HashSet<string> CourierTelemetryProperties =
        [nameof(Courier.LastLocationAtUtc), nameof(Courier.UpdatedAtUtc)];

    // Location pings arrive every few seconds per courier; auditing each LastLocationAtUtc bump would flood
    // audit_logs without recording any operator decision. Location history already lives in courier_locations.
    private static bool IsTelemetryOnlyChange(EntityEntry entry) =>
        entry is { Entity: Courier, State: EntityState.Modified } &&
        entry.Properties.Where(property => property.IsModified)
            .All(property => CourierTelemetryProperties.Contains(property.Metadata.Name));

    private Guid? TryRequestBusinessId()
    {
        try { return requestContext.BusinessId; }
        catch { return null; }
    }

    private static Guid? TryGuid(EntityEntry entry, string propertyName)
    {
        PropertyEntry? property = entry.Properties.FirstOrDefault(x => x.Metadata.Name == propertyName);
        return property?.CurrentValue is Guid value && value != Guid.Empty ? value : null;
    }

    private static readonly HashSet<string> RedactedAuditProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "CustomerName", "CustomerPhone", "DeliveryAddress", "CustomerSearchTokens", "PhoneNumber", "Address"
        , "DeliveryLatitude", "DeliveryLongitude", "DeliveryInstructions"
    };

    private static object? AuditValue(string propertyName, object? value)
    {
        if (RedactedAuditProperties.Contains(propertyName)) return "[REDACTED]";
        return value is Point point ? new { latitude = point.Y, longitude = point.X } : value;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreDbContext).Assembly);
        ValueConverter<string, string> piiConverter = new(
            value => orderPiiProtector.Protect(value),
            value => orderPiiProtector.Unprotect(value));
        ValueConverter<string?, string?> optionalPiiConverter = new(
            value => value == null ? null : orderPiiProtector.Protect(value),
            value => value == null ? null : orderPiiProtector.Unprotect(value));
        modelBuilder.Entity<Order>().Property(x => x.CustomerName).HasConversion(piiConverter).HasMaxLength(1000);
        modelBuilder.Entity<Order>().Property(x => x.CustomerPhone).HasConversion(piiConverter).HasMaxLength(500);
        modelBuilder.Entity<Order>().Property(x => x.DeliveryAddress).HasConversion(piiConverter).HasMaxLength(3000);
        modelBuilder.Entity<Order>().Property(x => x.DeliveryInstructions).HasConversion(optionalPiiConverter).HasMaxLength(2000);
        base.OnModelCreating(modelBuilder);
    }
}

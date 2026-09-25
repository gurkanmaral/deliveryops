using DeliveryOps.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Queries.Abstractions;

public interface ICoreDbContext
{
    DbSet<Business> Businesses { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Courier> Couriers { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderStatusHistory> OrderStatusHistory { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<CourierLocation> CourierLocations { get; }
    DbSet<CourierShift> CourierShifts { get; }
    DbSet<BusinessDispatchSettings> BusinessDispatchSettings { get; }
    DbSet<OrderDispatchState> OrderDispatchStates { get; }
    DbSet<DispatchAttempt> DispatchAttempts { get; }
    DbSet<NotificationOutboxMessage> NotificationOutbox { get; }
    DbSet<IntegrationOutboxMessage> IntegrationOutbox { get; }
    DbSet<ProviderOrderEventReceipt> ProviderOrderEventReceipts { get; }
    DbSet<BusinessSlaSettings> BusinessSlaSettings { get; }
    DbSet<OperationalAlert> OperationalAlerts { get; }
    DbSet<BusinessBillingSettings> BusinessBillingSettings { get; }
    DbSet<BillingSettlement> BillingSettlements { get; }
    DbSet<BusinessCreditAccount> BusinessCreditAccounts { get; }
    DbSet<CreditTransaction> CreditTransactions { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

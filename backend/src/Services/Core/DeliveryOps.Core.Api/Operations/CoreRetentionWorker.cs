using DeliveryOps.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.Api.Operations;

public sealed class CoreRetentionWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    IOptions<CoreRetentionOptions> options, ILogger<CoreRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        using PeriodicTimer timer = new(TimeSpan.FromHours(Math.Clamp(options.Value.ScanIntervalHours, 1, 168)), timeProvider);
        do
        {
            try { await PurgeAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Core retention cleanup failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        int reencrypted = await ReencryptLegacyPiiAsync(context, cancellationToken);
        DateTimeOffset piiCutoff = now.AddDays(-Math.Clamp(options.Value.OrderPiiDays, 1, 3650));
        OrderStatus[] terminalStatuses = [OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Returned];
        var ordersToAnonymize = await context.Orders
            .Where(x => x.PiiAnonymizedAtUtc == null && terminalStatuses.Contains(x.Status) &&
                        (x.UpdatedAtUtc ?? x.CreatedAtUtc) < piiCutoff)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(Math.Clamp(options.Value.OrderPiiBatchSize, 1, 2000))
            .ToListAsync(cancellationToken);
        foreach (var order in ordersToAnonymize) order.AnonymizePii(now);
        if (ordersToAnonymize.Count > 0) await context.SaveChangesAsync(cancellationToken);
        int locations = await context.CourierLocations
            .Where(x => x.RecordedAtUtc < now.AddDays(-Math.Clamp(options.Value.LocationDays, 1, 365)))
            .ExecuteDeleteAsync(cancellationToken);
        int audits = await context.AuditLogs
            .Where(x => x.CreatedAtUtc < now.AddDays(-Math.Clamp(options.Value.AuditDays, 30, 3650)))
            .ExecuteDeleteAsync(cancellationToken);
        DateTimeOffset outboxCutoff = now.AddDays(-Math.Clamp(options.Value.ProcessedOutboxDays, 1, 365));
        int notifications = await context.NotificationOutbox
            .Where(x => x.ProcessedAtUtc != null && x.ProcessedAtUtc < outboxCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        int integrations = await context.IntegrationOutbox
            .Where(x => x.ProcessedAtUtc != null && x.ProcessedAtUtc < outboxCutoff)
            .ExecuteDeleteAsync(cancellationToken);
        if (reencrypted + ordersToAnonymize.Count + locations + audits + notifications + integrations > 0)
            logger.LogInformation("Core privacy maintenance re-encrypted {Reencrypted} legacy orders, anonymized {Anonymized} orders and removed {Locations} locations, {Audits} audits, {Notifications} notification outbox and {Integrations} integration outbox rows.",
                reencrypted, ordersToAnonymize.Count, locations, audits, notifications, integrations);
    }

    private static async Task<int> ReencryptLegacyPiiAsync(CoreDbContext context, CancellationToken cancellationToken)
    {
        Guid[] ids = await context.Database.SqlQueryRaw<Guid>("""
            SELECT "Id" AS "Value"
            FROM orders
            WHERE "CustomerName" NOT LIKE 'enc:v1:%'
               OR "CustomerPhone" NOT LIKE 'enc:v1:%'
               OR "DeliveryAddress" NOT LIKE 'enc:v1:%'
            LIMIT 250
            """).ToArrayAsync(cancellationToken);
        if (ids.Length == 0) return 0;
        var orders = await context.Orders.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            context.Entry(order).Property(x => x.CustomerName).IsModified = true;
            context.Entry(order).Property(x => x.CustomerPhone).IsModified = true;
            context.Entry(order).Property(x => x.DeliveryAddress).IsModified = true;
        }
        await context.SavePiiMaintenanceChangesAsync(cancellationToken);
        return orders.Count;
    }
}

public sealed class CoreRetentionOptions
{
    public bool Enabled { get; init; } = true;
    public int LocationDays { get; init; } = 30;
    public int AuditDays { get; init; } = 365;
    public int ProcessedOutboxDays { get; init; } = 7;
    public int OrderPiiDays { get; init; } = 90;
    public int OrderPiiBatchSize { get; init; } = 500;
    public int ScanIntervalHours { get; init; } = 24;
}

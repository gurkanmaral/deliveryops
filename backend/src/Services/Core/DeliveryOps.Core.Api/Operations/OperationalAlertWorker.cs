using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Operations;
using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.Core.Queries.Operations;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Api.Operations;

public sealed class OperationalAlertWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    ILogger<OperationalAlertWorker> logger) : BackgroundService
{
    private static readonly OperationalAlertType[] ManagedTypes =
        [OperationalAlertType.CourierWaiting, OperationalAlertType.PickupDelayed,
         OperationalAlertType.DeliveryDelayed, OperationalAlertType.CourierLocationStale,
         OperationalAlertType.LowCreditBalance];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(4), stoppingToken);
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(15), timeProvider);
        do
        {
            try { await ProcessAllBusinessesAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Operational SLA evaluation failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessAllBusinessesAsync(CancellationToken cancellationToken)
    {
        Guid[] businessIds;
        await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            businessIds = await context.Businesses.AsNoTracking().Where(x => x.IsActive)
                .Select(x => x.Id).ToArrayAsync(cancellationToken);
        }
        foreach (Guid businessId in businessIds)
        {
            // A failure for one business must not stop SLA alerts for every other business.
            try { await ProcessBusinessAsync(businessId, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger.LogError(exception, "SLA evaluation failed for business {BusinessId}.", businessId); }
        }
    }

    private async Task ProcessBusinessAsync(Guid businessId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        IOperationalAlertNotifier notifier = scope.ServiceProvider.GetRequiredService<IOperationalAlertNotifier>();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({$"sla:{businessId}"}, 0))", cancellationToken);

        BusinessSlaSettings settings = await context.BusinessSlaSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == businessId, cancellationToken)
            ?? BusinessSlaSettings.CreateDefault(businessId);
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<AlertCandidate> detected = await DetectOrderAlertsAsync(context, settings, now, cancellationToken);
        detected.AddRange(await DetectCourierAlertsAsync(context, settings, now, cancellationToken));
        AlertCandidate? creditAlert = await DetectLowCreditAlertAsync(context, businessId, cancellationToken);
        if (creditAlert is not null) detected.Add(creditAlert);

        // Open alerts, plus resolved ones only when the same problem is detected again (they are reopened by
        // key); the rest of the resolved history does not need to be loaded every cycle.
        string[] detectedKeyList = detected.Select(x => x.Key).Distinct().ToArray();
        List<OperationalAlert> existing = await context.OperationalAlerts
            .Where(x => x.BusinessId == businessId && ManagedTypes.Contains(x.Type) &&
                        (x.Status != OperationalAlertStatus.Resolved || detectedKeyList.Contains(x.AlertKey)))
            .ToListAsync(cancellationToken);
        Dictionary<string, OperationalAlert> byKey = existing.ToDictionary(x => x.AlertKey);
        List<OperationalAlert> changed = [];

        foreach (AlertCandidate candidate in detected)
        {
            if (byKey.TryGetValue(candidate.Key, out OperationalAlert? alert))
            {
                if (alert.Refresh(candidate.Severity, candidate.Title, candidate.Message, now)) changed.Add(alert);
            }
            else
            {
                alert = OperationalAlert.Create(businessId, candidate.OrderId, candidate.CourierId,
                    candidate.Key, candidate.Type, candidate.Severity, candidate.Title, candidate.Message, now);
                context.OperationalAlerts.Add(alert);
                changed.Add(alert);
            }
        }

        HashSet<string> detectedKeys = detected.Select(x => x.Key).ToHashSet();
        foreach (OperationalAlert alert in existing.Where(x => !detectedKeys.Contains(x.AlertKey)))
            if (alert.Resolve(now)) changed.Add(alert);

        if (changed.Count > 0) await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (OperationalAlert alert in changed)
            await notifier.AlertChangedAsync(OperationalAlertMapper.Map(alert), cancellationToken);
    }

    private static async Task<List<AlertCandidate>> DetectOrderAlertsAsync(CoreDbContext context,
        BusinessSlaSettings settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        OrderStatus[] watchedStatuses =
            [OrderStatus.WaitingForCourier, OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.OnTheWay, OrderStatus.DeliveryFailed];
        List<Order> orders = await context.Orders.AsNoTracking()
            .Where(x => x.BusinessId == settings.BusinessId && watchedStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);
        Guid[] orderIds = orders.Select(x => x.Id).ToArray();
        OrderStatus[] transitionStatuses = [OrderStatus.WaitingForCourier, OrderStatus.Assigned, OrderStatus.PickedUp];
        var transitionRows = await context.OrderStatusHistory.AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId) && transitionStatuses.Contains(x.Status))
            .GroupBy(x => new { x.OrderId, x.Status })
            .Select(group => new { group.Key.OrderId, group.Key.Status, AtUtc = group.Min(x => x.CreatedAtUtc) })
            .ToListAsync(cancellationToken);
        Dictionary<(Guid, OrderStatus), DateTimeOffset> transitions = transitionRows
            .ToDictionary(x => (x.OrderId, x.Status), x => x.AtUtc);
        List<AlertCandidate> result = [];

        foreach (Order order in orders)
        {
            if (order.Status == OrderStatus.WaitingForCourier)
            {
                DateTimeOffset since = transitions.GetValueOrDefault((order.Id, OrderStatus.WaitingForCourier), order.CreatedAtUtc);
                AddOrderCandidate(result, order, now - since, settings.CourierWaitingWarningMinutes,
                    settings.CourierWaitingCriticalMinutes, OperationalAlertType.CourierWaiting,
                    "Kurye ataması gecikti", "kurye bekliyor");
            }
            else if (order.Status == OrderStatus.Assigned)
            {
                DateTimeOffset since = transitions.GetValueOrDefault((order.Id, OrderStatus.Assigned), order.CreatedAtUtc);
                AddOrderCandidate(result, order, now - since, settings.PickupWarningMinutes,
                    settings.PickupCriticalMinutes, OperationalAlertType.PickupDelayed,
                    "Paket teslim alınmadı", "atanmış durumda teslim alınmayı bekliyor");
            }
            else
            {
                DateTimeOffset since = transitions.GetValueOrDefault((order.Id, OrderStatus.PickedUp), order.CreatedAtUtc);
                AddOrderCandidate(result, order, now - since, settings.DeliveryWarningMinutes,
                    settings.DeliveryCriticalMinutes, OperationalAlertType.DeliveryDelayed,
                    "Teslimat gecikiyor", "teslimat sürecinde");
            }
        }
        return result;
    }

    private static void AddOrderCandidate(List<AlertCandidate> result, Order order, TimeSpan elapsed,
        int warningMinutes, int criticalMinutes, OperationalAlertType type, string title, string stateText)
    {
        OperationalAlertSeverity? severity = Severity(elapsed, warningMinutes, criticalMinutes);
        if (!severity.HasValue) return;
        int minutes = Math.Max(1, (int)Math.Floor(elapsed.TotalMinutes));
        result.Add(new AlertCandidate($"order:{order.Id}:{type}", type, severity.Value, order.Id, null,
            title, $"#{order.Id.ToString()[..8]} numaralı sipariş {minutes} dakikadır {stateText}."));
    }

    private static async Task<List<AlertCandidate>> DetectCourierAlertsAsync(CoreDbContext context,
        BusinessSlaSettings settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var couriers = await (from shift in context.CourierShifts.AsNoTracking()
                              join courier in context.Couriers.AsNoTracking() on shift.CourierId equals courier.Id
                              where shift.BusinessId == settings.BusinessId && shift.EndedAtUtc == null && courier.IsActive
                              select new { Courier = courier, shift.StartedAtUtc }).ToListAsync(cancellationToken);
        List<AlertCandidate> result = [];
        foreach (var item in couriers)
        {
            DateTimeOffset since = item.Courier.LastLocationAtUtc ?? item.StartedAtUtc;
            TimeSpan elapsed = now - since;
            OperationalAlertSeverity? severity = Severity(elapsed, settings.LocationStaleWarningMinutes,
                settings.LocationStaleCriticalMinutes);
            if (!severity.HasValue) continue;
            int minutes = Math.Max(1, (int)Math.Floor(elapsed.TotalMinutes));
            // Keys are globally unique and couriers can move between businesses, so the key carries the business.
            result.Add(new AlertCandidate(
                $"courier:{settings.BusinessId}:{item.Courier.Id}:{OperationalAlertType.CourierLocationStale}",
                OperationalAlertType.CourierLocationStale, severity.Value, null, item.Courier.Id,
                "Kurye konumu güncel değil",
                $"{item.Courier.FirstName} {item.Courier.LastName} için {minutes} dakikadır konum alınamadı."));
        }
        return result;
    }

    private static OperationalAlertSeverity? Severity(TimeSpan elapsed, int warningMinutes, int criticalMinutes)
    {
        if (elapsed.TotalMinutes >= criticalMinutes) return OperationalAlertSeverity.Critical;
        if (elapsed.TotalMinutes >= warningMinutes) return OperationalAlertSeverity.Warning;
        return null;
    }

    private static async Task<AlertCandidate?> DetectLowCreditAlertAsync(CoreDbContext context,
        Guid businessId, CancellationToken cancellationToken)
    {
        BusinessCreditAccount? account = await context.BusinessCreditAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == businessId, cancellationToken);
        if (account is null || account.LowBalanceThreshold == 0 || account.Balance > account.LowBalanceThreshold)
            return null;
        OperationalAlertSeverity severity = account.Balance == 0
            ? OperationalAlertSeverity.Critical
            : OperationalAlertSeverity.Warning;
        return new AlertCandidate($"business:{businessId}:{OperationalAlertType.LowCreditBalance}",
            OperationalAlertType.LowCreditBalance, severity, null, null,
            account.Balance == 0 ? "Kredi bakiyesi tükendi" : "Kredi bakiyesi azalıyor",
            $"Kalan kredi {account.Balance:N0}; uyarı eşiği {account.LowBalanceThreshold:N0}. Yeni siparişlerin durmaması için kredi yükleyin.");
    }

    private sealed record AlertCandidate(string Key, OperationalAlertType Type,
        OperationalAlertSeverity Severity, Guid? OrderId, Guid? CourierId, string Title, string Message);
}

using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Dispatch;
using DeliveryOps.Core.Handlers.Orders;
using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.Core.Queries.Orders;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Api.Dispatch;

public sealed class AutomaticDispatchWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<AutomaticDispatchWorker> logger) : BackgroundService
{
    private static readonly Guid SystemActorId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(3), timeProvider);
        do
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Automatic dispatch cycle failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope discoveryScope = scopeFactory.CreateAsyncScope();
        CoreDbContext context = discoveryScope.ServiceProvider.GetRequiredService<CoreDbContext>();
        DateTimeOffset now = timeProvider.GetUtcNow();
        Guid[] orderIds = await context.Orders.AsNoTracking()
            .Where(order => order.Status == OrderStatus.WaitingForCourier && order.CourierId == null &&
                            order.DeliveryFulfillment == DeliveryFulfillmentType.MerchantCourier)
            .Where(order => context.BusinessDispatchSettings.Any(settings =>
                settings.BusinessId == order.BusinessId && settings.AutoAssignCouriers))
            .Where(order => !context.OrderDispatchStates.Any(state => state.OrderId == order.Id) ||
                            context.OrderDispatchStates.Any(state => state.OrderId == order.Id &&
                                state.NextAttemptAtUtc != null && state.NextAttemptAtUtc <= now))
            .OrderBy(order => order.CreatedAtUtc)
            .Select(order => order.Id)
            .Take(25)
            .ToArrayAsync(cancellationToken);

        foreach (Guid orderId in orderIds)
        {
            // Isolate failures: the batch is ordered oldest-first, so one order that keeps throwing would
            // otherwise abort every cycle and stop dispatch for all businesses.
            try { await TryDispatchAsync(orderId, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic dispatch failed for order {OrderId}; backing off.", orderId);
                await BackOffFailedOrderAsync(orderId, cancellationToken);
            }
        }
    }

    private async Task BackOffFailedOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Order? order = await context.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
            if (order is null) return;
            OrderDispatchState? state = await context.OrderDispatchStates.SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);
            if (state is null)
            {
                state = OrderDispatchState.Create(order.Id, order.BusinessId);
                context.OrderDispatchStates.Add(state);
            }
            const string reason = "Otomatik atama sırasında beklenmeyen bir hata oluştu; tekrar denenecek.";
            state.MarkNoCourier(timeProvider.GetUtcNow(), reason);
            context.DispatchAttempts.Add(DispatchAttempt.Create(order.Id, order.BusinessId, null, "Automatic", false, reason));
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not record dispatch back-off for order {OrderId}.", orderId);
        }
    }

    private async Task TryDispatchAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        IOrderOperationsNotifier notifier = scope.ServiceProvider.GetRequiredService<IOrderOperationsNotifier>();
        DispatchCandidateFinder candidateFinder = scope.ServiceProvider.GetRequiredService<DispatchCandidateFinder>();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        Order? order = await context.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return;

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({order.BusinessId.ToString()}, 0))", cancellationToken);
        await context.Entry(order).ReloadAsync(cancellationToken);
        if (order.Status != OrderStatus.WaitingForCourier || order.CourierId.HasValue) return;

        BusinessDispatchSettings? settings = await context.BusinessDispatchSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == order.BusinessId, cancellationToken);
        if (settings?.AutoAssignCouriers != true) return;

        DateTimeOffset dispatchNow = timeProvider.GetUtcNow();
        var suggestions = await candidateFinder.FindAsync(order, settings, cancellationToken);
        var selected = suggestions.FirstOrDefault();

        OrderDispatchState? state = await context.OrderDispatchStates.SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);
        if (state is null)
        {
            state = OrderDispatchState.Create(order.Id, order.BusinessId);
            context.OrderDispatchStates.Add(state);
        }

        if (selected is null)
        {
            const string reason = "Aktif mesai, kapasite ve konum kurallarına uyan bir kurye bulunamadı.";
            state.MarkNoCourier(dispatchNow, reason);
            context.DispatchAttempts.Add(DispatchAttempt.Create(order.Id, order.BusinessId, null,
                "Automatic", false, reason));
        }
        else
        {
            Courier courier = await context.Couriers.SingleAsync(x => x.Id == selected.CourierId, cancellationToken);
            courier.ReserveAssignmentSlot();
            order.AssignCourier(courier.Id, SystemActorId);
            context.OrderStatusHistory.Add(order.StatusHistory.Single());
            if (courier.DeliveryStatus == DeliveryStatus.WaitingForAssignment)
                courier.SetDeliveryStatus(DeliveryStatus.GoingToPickup);
            state.MarkAssigned(dispatchNow, courier.Id);
            context.DispatchAttempts.Add(DispatchAttempt.Create(order.Id, order.BusinessId, courier.Id,
                "Automatic", true, null));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogInformation("Dispatch concurrency conflict for order {OrderId}; it will be retried.", orderId);
            return;
        }

        OrderResponse response = GetOrderHandler.ApplyDispatchState(OrderMapper.Map(order), state);
        await notifier.OrderChangedAsync(response, cancellationToken);
    }
}

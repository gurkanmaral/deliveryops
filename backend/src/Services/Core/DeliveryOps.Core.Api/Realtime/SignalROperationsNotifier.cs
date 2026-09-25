using DeliveryOps.Core.Queries.Locations;
using DeliveryOps.Core.Queries.Orders;
using Microsoft.AspNetCore.SignalR;
using DeliveryOps.Core.Queries.Operations;

namespace DeliveryOps.Core.Api.Realtime;

public sealed class SignalROperationsNotifier(IHubContext<OperationsHub> hub) : IOperationsNotifier, IOrderOperationsNotifier, IOperationalAlertNotifier
{
    public async Task LocationUpdatedAsync(CourierLocationSnapshot snapshot, CancellationToken cancellationToken)
    {
        await hub.Clients.Group($"business:{snapshot.BusinessId}").SendAsync("courierLocationUpdated", snapshot, cancellationToken);
        await hub.Clients.Group("platform").SendAsync("courierLocationUpdated", snapshot, cancellationToken);
    }

    public async Task LocationStaleAsync(Guid courierId, Guid businessId, CancellationToken cancellationToken)
    {
        var message = new { courierId, businessId, staleAtUtc = DateTimeOffset.UtcNow };
        await hub.Clients.Group($"business:{businessId}").SendAsync("courierLocationStale", message, cancellationToken);
        await hub.Clients.Group("platform").SendAsync("courierLocationStale", message, cancellationToken);
    }

    public async Task OrderChangedAsync(OrderResponse order, CancellationToken cancellationToken)
    {
        await hub.Clients.Group($"business:{order.BusinessId}").SendAsync("orderChanged", order, cancellationToken);
        await hub.Clients.Group("platform").SendAsync("orderChanged", order, cancellationToken);
        if (order.CourierId.HasValue)
            await hub.Clients.Group($"courier:{order.CourierId}").SendAsync("orderChanged", order, cancellationToken);
    }

    public async Task OrderReassignedAsync(OrderResponse order, Guid previousCourierId, CancellationToken cancellationToken)
    {
        await OrderChangedAsync(order, cancellationToken);
        await hub.Clients.Group($"courier:{previousCourierId}").SendAsync("orderRemoved", new
        {
            orderId = order.Id,
            reason = "Sipariş başka bir kuryeye atandı."
        }, cancellationToken);
    }

    public async Task AlertChangedAsync(OperationalAlertResponse alert, CancellationToken cancellationToken)
    {
        await hub.Clients.Group($"business:{alert.BusinessId}").SendAsync("operationalAlertChanged", alert, cancellationToken);
        await hub.Clients.Group("platform").SendAsync("operationalAlertChanged", alert, cancellationToken);
    }
}

using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Queries.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Common;

internal static class CourierWorkload
{
    public static readonly OrderStatus[] ActiveOrderStatuses =
        [OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.OnTheWay, OrderStatus.DeliveryFailed];

    public static Task<bool> HasActiveOrdersAsync(ICoreDbContext context, Guid courierId,
        CancellationToken cancellationToken) =>
        context.Orders.AnyAsync(x => x.CourierId == courierId && ActiveOrderStatuses.Contains(x.Status),
            cancellationToken);
}

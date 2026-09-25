using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Dashboard;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Dashboard;

public sealed class GetDashboardSummaryHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetDashboardSummaryQuery, DashboardSummary>
{
    public async Task<DashboardSummary> Handle(
        GetDashboardSummaryQuery request,
        CancellationToken cancellationToken)
    {
        Guid? businessId = requestContext.IsPlatformAdmin ? null : requestContext.BusinessId;
        IQueryable<Courier> couriers = context.Couriers;
        IQueryable<Order> orders = context.Orders;
        if (businessId.HasValue)
        {
            couriers = couriers.Where(x => x.BusinessId == businessId.Value);
            orders = orders.Where(x => x.BusinessId == businessId.Value);
        }

        int activeBusinesses = requestContext.IsPlatformAdmin
            ? await context.Businesses.CountAsync(x => x.IsActive, cancellationToken)
            : businessId.HasValue ? 1 : 0;
        int activeCouriers = await couriers.CountAsync(x => x.IsActive && x.Availability == CourierAvailability.Available, cancellationToken);
        int openOrders = await orders.CountAsync(x => x.Status != OrderStatus.Delivered && x.Status != OrderStatus.Cancelled, cancellationToken);
        int deliveringCouriers = await couriers.CountAsync(x => x.IsActive && x.DeliveryStatus == DeliveryStatus.Delivering, cancellationToken);

        return new DashboardSummary(activeBusinesses, activeCouriers, openOrders, deliveringCouriers);
    }
}

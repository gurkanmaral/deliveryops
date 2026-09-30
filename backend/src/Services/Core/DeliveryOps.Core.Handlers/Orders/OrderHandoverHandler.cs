using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Orders;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Orders;

/// <summary>
/// Completes an order that leaves the store without our courier: handed to the platform's courier
/// (e.g. Getir, which then tracks delivery itself) or collected by the customer (gel-al). For a Getir
/// order this triggers the handover call to Getir through the integration outbox.
/// </summary>
public sealed class CompleteOrderHandoverHandler(ICoreDbContext context, IRequestContext requestContext,
    IOrderOperationsNotifier notifier)
    : IRequestHandler<CompleteOrderHandoverCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(CompleteOrderHandoverCommand request, CancellationToken cancellationToken)
    {
        Order? order = await context.Orders.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (order is null) return Result<OrderResponse>.Failure(HandlerErrors.NotFound("Sipariş"));
        if (!TenantAccess.CanAccessBranch(requestContext, order.BusinessId, order.BranchId) || requestContext.CourierId.HasValue)
            return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        if (order.DeliveryFulfillment == DeliveryFulfillmentType.MerchantCourier)
            return Result<OrderResponse>.Failure(HandlerErrors.Validation("Bu sipariş işletme kuryesiyle teslim edilir; kurye akışını kullanın."));
        if (order.Status == OrderStatus.Delivered) return Result<OrderResponse>.Success(OrderMapper.Map(order));
        if (order.Status is not (OrderStatus.New or OrderStatus.Confirmed or OrderStatus.WaitingForCourier or OrderStatus.OnTheWay))
            return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Bu durumdaki sipariş teslim edilemez."));
        try { order.ApplyProviderDeliveryStatus(OrderStatus.Delivered, requestContext.UserId); }
        catch (InvalidOperationException exception) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict(exception.Message)); }
        context.OrderStatusHistory.AddRange(order.StatusHistory);
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Sipariş başka bir işlem tarafından güncellendi.")); }
        OrderResponse response = OrderMapper.Map(order);
        await notifier.OrderChangedAsync(response, cancellationToken);
        return Result<OrderResponse>.Success(response);
    }
}

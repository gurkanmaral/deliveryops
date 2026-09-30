using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Orders;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Orders;

internal static class OrderPayments
{
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>Applies the payment details that arrive together with a new order.</summary>
    public static Error? ApplyInitial(Order order, OrderPaymentInput? input, DateTimeOffset now)
    {
        if (input is null) return null;
        if (!Enum.IsDefined(input.Method)) return HandlerErrors.Validation("Ödeme yöntemi geçersiz.");
        if (input.IsPaid && input.Method == PaymentMethod.Unspecified)
            return HandlerErrors.Validation("Ödendi olarak gönderilen siparişte ödeme yöntemi zorunludur.");
        if (input.PaidAtUtc > now.Add(AllowedClockSkew))
            return HandlerErrors.Validation("Ödeme zamanı gelecekte olamaz.");
        order.SetPaymentMethod(input.Method);
        // Online orders were already charged by the provider or web shop; counter orders sent as paid were
        // charged on the POS before being forwarded.
        bool paid = input.IsPaid || input.Method == PaymentMethod.Online;
        decimal amount = input.Amount ?? order.TotalAmount;
        if (!paid || amount <= 0) return null;
        try
        {
            order.RecordPayment(input.Method, amount,
                input.Method == PaymentMethod.Online ? PaymentChannel.Provider : PaymentChannel.Counter,
                input.Reference, input.PaidAtUtc ?? now);
        }
        catch (ArgumentException exception) { return HandlerErrors.Validation(exception.Message); }
        return null;
    }

    /// <summary>Cash or card on delivery is collected by the order's courier when the order is delivered.</summary>
    public static void CollectOnDelivery(Order order, DateTimeOffset now)
    {
        if (!order.RequiresCollectionAtDoor || !order.CourierId.HasValue || order.TotalAmount <= 0) return;
        order.RecordPayment(order.PaymentMethod, order.TotalAmount, PaymentChannel.Courier, null, now,
            order.CourierId.Value);
    }

    public static async Task RecordDeliveryDistanceAsync(ICoreDbContext context, Order order, Guid courierId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!order.DeliveryLatitude.HasValue || !order.DeliveryLongitude.HasValue ||
            order.DeliveryLocationAccuracy != DeliveryLocationAccuracy.Exact) return;
        DateTimeOffset freshAfter = now.AddMinutes(-10);
        CourierLocation? location = await context.CourierLocations.AsNoTracking()
            .Where(x => x.CourierId == courierId && x.RecordedAtUtc >= freshAfter)
            .OrderByDescending(x => x.RecordedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (location is null) return;
        double? distanceKm = Domain.Dispatch.CourierAssignmentRanker.CalculateDistanceKm(location.Position.Y,
            location.Position.X, order.DeliveryLatitude, order.DeliveryLongitude);
        if (distanceKm.HasValue) order.RecordDeliveryDistance(distanceKm.Value * 1000);
    }
}

public sealed class RecordOrderPaymentHandler(ICoreDbContext context, IRequestContext requestContext,
    IOrderOperationsNotifier notifier, TimeProvider timeProvider)
    : IRequestHandler<RecordOrderPaymentCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(RecordOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        Order? order = await context.Orders.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (order is null) return Result<OrderResponse>.Failure(HandlerErrors.NotFound("Sipariş"));
        if (!TenantAccess.CanAccessBranch(requestContext, order.BusinessId, order.BranchId) || requestContext.CourierId.HasValue)
            return Result<OrderResponse>.Failure(HandlerErrors.Forbidden);
        if (request.Method == PaymentMethod.Unspecified || !Enum.IsDefined(request.Method))
            return Result<OrderResponse>.Failure(HandlerErrors.Validation("Ödeme yöntemi seçilmelidir."));
        if (order.PaymentStatus == PaymentStatus.Paid) return Result<OrderResponse>.Success(OrderMapper.Map(order));
        try
        {
            order.RecordPayment(request.Method, request.Amount ?? order.TotalAmount, PaymentChannel.Panel,
                request.Reference, timeProvider.GetUtcNow());
        }
        catch (ArgumentException exception) { return Result<OrderResponse>.Failure(HandlerErrors.Validation(exception.Message)); }
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Result<OrderResponse>.Failure(HandlerErrors.Conflict("Sipariş başka bir işlem tarafından güncellendi.")); }
        OrderResponse response = OrderMapper.Map(order);
        await notifier.OrderChangedAsync(response, cancellationToken);
        return Result<OrderResponse>.Success(response);
    }
}

public sealed class GetCourierShiftSummaryHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetCourierShiftSummaryQuery, Result<CourierShiftSummaryResponse>>
{
    public async Task<Result<CourierShiftSummaryResponse>> Handle(GetCourierShiftSummaryQuery request,
        CancellationToken cancellationToken)
    {
        if (!requestContext.CourierId.HasValue) return Result<CourierShiftSummaryResponse>.Failure(HandlerErrors.Forbidden);
        Guid courierId = requestContext.CourierId.Value;
        CourierShift? shift = await context.CourierShifts.AsNoTracking()
            .Where(x => x.CourierId == courierId && x.EndedAtUtc == null)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        int activeOrders = await context.Orders.CountAsync(x => x.CourierId == courierId &&
            CourierWorkload.ActiveOrderStatuses.Contains(x.Status), cancellationToken);
        if (shift is null)
            return Result<CourierShiftSummaryResponse>.Success(new(courierId, false, null, 0, activeOrders, 0, 0, 0, "TRY"));

        DateTimeOffset since = shift.StartedAtUtc;
        int delivered = await context.OrderStatusHistory.AsNoTracking()
            .Where(x => x.Status == OrderStatus.Delivered && x.CreatedAtUtc >= since && x.Order.CourierId == courierId)
            .Select(x => x.OrderId).Distinct().CountAsync(cancellationToken);
        var collections = await context.Orders.AsNoTracking()
            .Where(x => x.PaymentCollectedByCourierId == courierId && x.PaidAtUtc >= since)
            .GroupBy(x => x.PaymentMethod)
            .Select(group => new { Method = group.Key, Total = group.Sum(x => x.PaidAmount ?? 0), Count = group.Count() })
            .ToListAsync(cancellationToken);
        decimal cash = collections.Where(x => x.Method == PaymentMethod.Cash).Sum(x => x.Total);
        decimal card = collections.Where(x => x.Method == PaymentMethod.Card).Sum(x => x.Total);
        return Result<CourierShiftSummaryResponse>.Success(new(courierId, true, since, delivered, activeOrders,
            cash, card, collections.Sum(x => x.Count), "TRY"));
    }
}

using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Orders;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Orders;

public sealed class ApplyProviderOrderEventHandler(ICoreDbContext context, IRequestContext requestContext,
    IOrderOperationsNotifier notifier)
    : IRequestHandler<ApplyProviderOrderEventCommand, Result<ProviderOrderEventResponse>>
{
    public async Task<Result<ProviderOrderEventResponse>> Handle(ApplyProviderOrderEventCommand request,
        CancellationToken cancellationToken)
    {
        if (request.BusinessId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExternalOrderId) ||
            string.IsNullOrWhiteSpace(request.ExternalEventId) || string.IsNullOrWhiteSpace(request.ProviderStatus))
            return Result<ProviderOrderEventResponse>.Failure(HandlerErrors.Validation("Provider olay bilgileri eksik."));

        ProviderOrderEventReceipt? receipt = await context.ProviderOrderEventReceipts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == request.BusinessId && x.Source == request.Source &&
                x.ExternalEventId == request.ExternalEventId.Trim(), cancellationToken);
        if (receipt is not null)
        {
            OrderStatus current = await context.Orders.AsNoTracking().Where(x => x.Id == receipt.OrderId)
                .Select(x => x.Status).SingleAsync(cancellationToken);
            return Result<ProviderOrderEventResponse>.Success(new(receipt.OrderId, current, true,
                false, receipt.Outcome));
        }

        Order? order = await context.Orders.SingleOrDefaultAsync(x => x.BusinessId == request.BusinessId &&
            x.Source == request.Source && x.ExternalId == request.ExternalOrderId.Trim(), cancellationToken);
        if (order is null)
            return Result<ProviderOrderEventResponse>.Failure(HandlerErrors.NotFound("Sağlayıcı siparişi"));

        string providerStatus = request.ProviderStatus.Trim().ToUpperInvariant();
        string outcome;
        bool changed = false;
        bool creditRefunded = false;
        try
        {
            (outcome, changed) = providerStatus switch
            {
                "READY_FOR_PICKUP" => ApplyReadyForPickup(order, requestContext.UserId),
                "DISPATCHED" => ApplyDispatched(order, requestContext.UserId),
                "DELIVERED" => ApplyDelivered(order, requestContext.UserId),
                "CANCELLED" => ApplyCancelled(order, request.CancellationReason, requestContext.UserId),
                _ => throw new ArgumentException($"Desteklenmeyen sağlayıcı durumu: {providerStatus}.")
            };
        }
        catch (ArgumentException exception)
        {
            return Result<ProviderOrderEventResponse>.Failure(HandlerErrors.Validation(exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result<ProviderOrderEventResponse>.Failure(HandlerErrors.Conflict(exception.Message));
        }

        if (changed)
            context.OrderStatusHistory.AddRange(order.StatusHistory);

        if (changed && providerStatus == "CANCELLED")
        {
            Result<bool> refund = await RefundCreditAsync(order, cancellationToken);
            if (refund.IsFailure) return Result<ProviderOrderEventResponse>.Failure(refund.Error);
            creditRefunded = refund.Value;
            await CancelOrderHandler.ReleaseCourierAsync(context, order, cancellationToken);
        }

        context.ProviderOrderEventReceipts.Add(ProviderOrderEventReceipt.Create(order.BusinessId, order.Id,
            order.Source, request.ExternalEventId, providerStatus, outcome));
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ProviderOrderEventResponse>.Failure(
                HandlerErrors.Conflict("Sipariş başka bir işlem tarafından güncellendi."));
        }
        catch (DbUpdateException)
        {
            return Result<ProviderOrderEventResponse>.Failure(
                HandlerErrors.Conflict("Sağlayıcı olayı eş zamanlı olarak işlendi."));
        }

        if (changed) await notifier.OrderChangedAsync(OrderMapper.Map(order), cancellationToken);
        return Result<ProviderOrderEventResponse>.Success(new(order.Id, order.Status, false,
            creditRefunded, outcome));
    }

    private async Task<Result<bool>> RefundCreditAsync(Order order, CancellationToken cancellationToken)
    {
        if (await context.CreditTransactions.AnyAsync(x => x.BusinessId == order.BusinessId &&
                x.OrderId == order.Id && x.Type == CreditTransactionType.Refund, cancellationToken))
            return Result<bool>.Success(false);
        CreditTransaction? consumption = await context.CreditTransactions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == order.BusinessId && x.OrderId == order.Id &&
                x.Type == CreditTransactionType.OrderConsumption, cancellationToken);
        // Nothing was consumed, so there is nothing to refund; the provider cancellation itself must still apply.
        if (consumption is null) return Result<bool>.Success(false);
        BusinessCreditAccount? account = await context.BusinessCreditAccounts
            .SingleOrDefaultAsync(x => x.BusinessId == order.BusinessId, cancellationToken);
        if (account is null)
            return Result<bool>.Failure(HandlerErrors.Conflict("İşletmenin kredi hesabı bulunmuyor."));
        int amount = Math.Abs(consumption.Amount);
        int balance = account.Refund(amount);
        context.CreditTransactions.Add(CreditTransaction.Create(order.BusinessId,
            CreditTransactionType.Refund, amount, balance, order.Id,
            $"Sağlayıcı iptali nedeniyle sipariş #{order.Id.ToString("N")[..8]} kredisi iade edildi.",
            requestContext.UserId));
        return Result<bool>.Success(true);
    }

    private static (string Outcome, bool Changed) ApplyReadyForPickup(Order order, Guid userId)
    {
        if (order.DeliveryFulfillment != DeliveryFulfillmentType.MerchantCourier)
        {
            if (order.Status == OrderStatus.New)
            {
                order.ChangeStatus(OrderStatus.Confirmed, userId);
                return ("Applied", true);
            }
            return ("AlreadyAdvanced", false);
        }
        if (order.Status == OrderStatus.New) order.ChangeStatus(OrderStatus.Confirmed, userId);
        if (order.Status == OrderStatus.Confirmed)
        {
            order.ChangeStatus(OrderStatus.WaitingForCourier, userId);
            return ("Applied", true);
        }
        if (order.Status == OrderStatus.WaitingForCourier) return ("AlreadyApplied", false);
        return ("AlreadyAdvanced", false);
    }

    private static (string Outcome, bool Changed) ApplyDispatched(Order order, Guid userId)
    {
        if (order.DeliveryFulfillment != DeliveryFulfillmentType.MerchantCourier)
        {
            if (order.Status is OrderStatus.OnTheWay or OrderStatus.Delivered) return ("AlreadyApplied", false);
            order.ApplyProviderDeliveryStatus(OrderStatus.OnTheWay, userId);
            return ("Applied", true);
        }
        if (order.Status == OrderStatus.PickedUp)
        {
            order.ChangeStatus(OrderStatus.OnTheWay, userId);
            return ("Applied", true);
        }
        if (order.Status is OrderStatus.OnTheWay or OrderStatus.Delivered) return ("AlreadyApplied", false);
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Returned) return ("IgnoredTerminal", false);
        throw new InvalidOperationException($"DISPATCHED olayı {order.Status} durumundaki siparişe henüz uygulanamaz.");
    }

    private static (string Outcome, bool Changed) ApplyDelivered(Order order, Guid userId)
    {
        if (order.DeliveryFulfillment != DeliveryFulfillmentType.MerchantCourier)
        {
            if (order.Status == OrderStatus.Delivered) return ("AlreadyApplied", false);
            order.ApplyProviderDeliveryStatus(OrderStatus.Delivered, userId);
            return ("Applied", true);
        }
        if (order.Status == OrderStatus.OnTheWay)
        {
            order.ChangeStatus(OrderStatus.Delivered, userId);
            return ("Applied", true);
        }
        if (order.Status == OrderStatus.Delivered) return ("AlreadyApplied", false);
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Returned) return ("IgnoredTerminal", false);
        throw new InvalidOperationException($"DELIVERED olayı {order.Status} durumundaki siparişe henüz uygulanamaz.");
    }

    private static (string Outcome, bool Changed) ApplyCancelled(Order order, string? reason, Guid userId)
    {
        if (order.Status == OrderStatus.Cancelled) return ("AlreadyApplied", false);
        if (order.Status is OrderStatus.Delivered or OrderStatus.Returned) return ("IgnoredTerminal", false);
        order.Cancel(string.IsNullOrWhiteSpace(reason) ? "Sağlayıcı tarafından iptal edildi." : reason, userId);
        return ("Applied", true);
    }
}

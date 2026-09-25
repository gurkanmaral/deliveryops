using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Enums;
using MediatR;

namespace DeliveryOps.Core.Queries.Orders;

public sealed record OrderResponse(
    Guid Id,
    Guid BusinessId,
    Guid BranchId,
    Guid? CourierId,
    string ExternalId,
    string CustomerName,
    string CustomerPhone,
    string DeliveryAddress,
    OrderSource Source,
    OrderStatus Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAtUtc,
    string? CancellationReason,
    string? DeliveryFailureReason,
    IReadOnlyList<OrderStatus> AllowedNextStatuses,
    DispatchStatus? DispatchStatus = null,
    int DispatchAttemptCount = 0,
    string? DispatchLastReason = null,
    DateTimeOffset? DispatchNextAttemptAtUtc = null,
    double? DeliveryLatitude = null,
    double? DeliveryLongitude = null,
    string? DeliveryInstructions = null,
    DeliveryLocationSource DeliveryLocationSource = DeliveryLocationSource.Unknown,
    DeliveryLocationAccuracy DeliveryLocationAccuracy = DeliveryLocationAccuracy.Unknown,
    DeliveryFulfillmentType DeliveryFulfillment = DeliveryFulfillmentType.MerchantCourier);

public sealed record AvailableOrderResponse(
    Guid Id,
    Guid BusinessId,
    Guid BranchId,
    string PickupName,
    string PickupAddress,
    double PickupDistanceKm,
    OrderSource Source,
    OrderStatus Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAtUtc,
    int WaitingMinutes);

public sealed record GetOrdersQuery(
    Guid? BusinessId,
    Guid? BranchId,
    Guid? CourierId,
    OrderStatus? Status,
    OrderSource? Source,
    DateOnly? CreatedFrom,
    DateOnly? CreatedTo,
    decimal? MinAmount,
    decimal? MaxAmount,
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    string Sort = "-created")
    : IRequest<Result<PagedResponse<OrderResponse>>>;
public sealed record GetOrderQuery(Guid Id) : IRequest<Result<OrderResponse>>;
public sealed record CreateOrderCommand(
    Guid BusinessId,
    Guid BranchId,
    string ExternalId,
    string CustomerName,
    string CustomerPhone,
    string DeliveryAddress,
    OrderSource Source,
    decimal TotalAmount,
    string IdempotencyKey,
    bool IsTrustedIntegration = false,
    double? DeliveryLatitude = null,
    double? DeliveryLongitude = null,
    string? DeliveryInstructions = null,
    DeliveryLocationSource DeliveryLocationSource = DeliveryLocationSource.Unknown,
    DeliveryLocationAccuracy DeliveryLocationAccuracy = DeliveryLocationAccuracy.Unknown,
    DeliveryFulfillmentType DeliveryFulfillment = DeliveryFulfillmentType.MerchantCourier) : IRequest<Result<OrderResponse>>;
public sealed record UpdateOrderCommand(Guid Id, string CustomerName, string CustomerPhone, string DeliveryAddress, decimal TotalAmount)
    : IRequest<Result<OrderResponse>>;
public sealed record AssignOrderCourierCommand(Guid Id, Guid CourierId) : IRequest<Result<OrderResponse>>;
public sealed record ClaimOrderCommand(Guid Id) : IRequest<Result<OrderResponse>>;
public sealed record GetAvailableOrdersQuery(int Page = 1, int PageSize = 25) : IRequest<Result<PagedResponse<AvailableOrderResponse>>>;
public sealed record ChangeOrderStatusCommand(Guid Id, OrderStatus Status) : IRequest<Result<OrderResponse>>;
public sealed record CancelOrderCommand(Guid Id, string? Reason) : IRequest<Result<OrderResponse>>;
public sealed record ReportDeliveryFailureCommand(Guid Id, string Reason) : IRequest<Result<OrderResponse>>;
public sealed record ApplyProviderOrderEventCommand(Guid BusinessId, string ExternalOrderId,
    OrderSource Source, string ExternalEventId, string ProviderStatus, string? CancellationReason)
    : IRequest<Result<ProviderOrderEventResponse>>;
public sealed record ProviderOrderEventResponse(Guid OrderId, OrderStatus Status, bool Duplicate,
    bool CreditRefunded, string Outcome);

public interface IOrderOperationsNotifier
{
    Task OrderChangedAsync(OrderResponse order, CancellationToken cancellationToken);
    Task OrderReassignedAsync(OrderResponse order, Guid previousCourierId, CancellationToken cancellationToken);
}

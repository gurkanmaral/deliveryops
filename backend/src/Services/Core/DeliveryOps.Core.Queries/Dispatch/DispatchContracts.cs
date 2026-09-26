using DeliveryOps.BuildingBlocks.Application;
using MediatR;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.Queries.Dispatch;

public sealed record DispatchSettingsResponse(
    Guid BusinessId,
    bool AutoConfirmOrders,
    bool AutoAssignCouriers,
    bool AllowCourierSelfClaim,
    bool PreferBranchCouriers,
    int MaxActiveOrdersPerCourier,
    bool RequireFreshLocation,
    int LocationFreshnessMinutes,
    double? AssignmentRadiusKm,
    bool PreferDeliveryClusters,
    double DeliveryClusterRadiusKm);

public sealed record GetDispatchSettingsQuery(Guid? BusinessId) : IRequest<Result<DispatchSettingsResponse>>;

public sealed record UpdateDispatchSettingsCommand(
    Guid? BusinessId,
    bool AutoConfirmOrders,
    bool AutoAssignCouriers,
    bool AllowCourierSelfClaim,
    bool PreferBranchCouriers,
    int MaxActiveOrdersPerCourier,
    bool RequireFreshLocation,
    int LocationFreshnessMinutes,
    double? AssignmentRadiusKm,
    bool PreferDeliveryClusters,
    double DeliveryClusterRadiusKm) : IRequest<Result<DispatchSettingsResponse>>;

public sealed record DispatchQueueItemResponse(
    Guid OrderId,
    Guid BusinessId,
    Guid BranchId,
    string CustomerName,
    string DeliveryAddress,
    DateTimeOffset CreatedAtUtc,
    DispatchStatus Status,
    int AttemptCount,
    string? LastReason,
    DateTimeOffset? NextAttemptAtUtc);

public sealed record CourierSuggestionResponse(
    int Rank,
    Guid CourierId,
    string CourierName,
    Guid? BranchId,
    bool IsBranchCourier,
    int ActiveOrderCount,
    double? DistanceKm,
    DateTimeOffset? LocationRecordedAtUtc,
    double? DeliveryClusterDistanceKm = null);

public sealed record DispatchAttemptResponse(
    Guid Id,
    Guid OrderId,
    Guid? CourierId,
    string? CourierName,
    string Trigger,
    bool WasSuccessful,
    string? Reason,
    DateTimeOffset CreatedAtUtc);

public sealed record GetDispatchQueueQuery(Guid? BusinessId, int Page = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<DispatchQueueItemResponse>>>;
public sealed record GetCourierSuggestionsQuery(Guid OrderId, int Page = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<CourierSuggestionResponse>>>;
public sealed record GetDispatchAttemptsQuery(Guid OrderId, int Page = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<DispatchAttemptResponse>>>;
public sealed record RetryDispatchCommand(Guid OrderId) : IRequest<Result>;

using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Enums;
using MediatR;

namespace DeliveryOps.Core.Queries.Operations;

public sealed record SlaSettingsResponse(Guid BusinessId,
    int CourierWaitingWarningMinutes, int CourierWaitingCriticalMinutes,
    int PickupWarningMinutes, int PickupCriticalMinutes,
    int DeliveryWarningMinutes, int DeliveryCriticalMinutes,
    int LocationStaleWarningMinutes, int LocationStaleCriticalMinutes);

public sealed record OperationalAlertResponse(Guid Id, Guid BusinessId, Guid? OrderId, Guid? CourierId,
    OperationalAlertType Type, OperationalAlertSeverity Severity, OperationalAlertStatus Status,
    string Title, string Message, DateTimeOffset FirstDetectedAtUtc, DateTimeOffset LastChangedAtUtc,
    DateTimeOffset? AcknowledgedAtUtc, DateTimeOffset? ResolvedAtUtc);

public sealed record GetSlaSettingsQuery(Guid? BusinessId) : IRequest<Result<SlaSettingsResponse>>;
public sealed record UpdateSlaSettingsCommand(Guid? BusinessId,
    int CourierWaitingWarningMinutes, int CourierWaitingCriticalMinutes,
    int PickupWarningMinutes, int PickupCriticalMinutes,
    int DeliveryWarningMinutes, int DeliveryCriticalMinutes,
    int LocationStaleWarningMinutes, int LocationStaleCriticalMinutes) : IRequest<Result<SlaSettingsResponse>>;
public sealed record GetOperationalAlertsQuery(Guid? BusinessId, bool IncludeResolved = false,
    OperationalAlertSeverity? MinimumSeverity = null, int Page = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<OperationalAlertResponse>>>;
public sealed record AcknowledgeOperationalAlertCommand(Guid Id) : IRequest<Result<OperationalAlertResponse>>;

public interface IOperationalAlertNotifier
{
    Task AlertChangedAsync(OperationalAlertResponse alert, CancellationToken cancellationToken);
}

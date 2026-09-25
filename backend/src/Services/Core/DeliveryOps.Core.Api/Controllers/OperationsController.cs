using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Queries.Operations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/operations")]
public sealed class OperationsController(ISender sender) : ApiControllerBase
{
    [HttpGet("alerts")]
    [Authorize(Policy = Permissions.DispatchRead)]
    public async Task<ActionResult<PagedResponse<OperationalAlertResponse>>> GetAlerts([FromQuery] Guid? businessId,
        [FromQuery] bool includeResolved = false, [FromQuery] OperationalAlertSeverity? minimumSeverity = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetOperationalAlertsQuery(businessId, includeResolved, minimumSeverity,
            page, pageSize), cancellationToken));

    [HttpPost("alerts/{id:guid}/acknowledge")]
    [Authorize(Policy = Permissions.DispatchWrite)]
    public async Task<ActionResult<OperationalAlertResponse>> Acknowledge(Guid id, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new AcknowledgeOperationalAlertCommand(id), cancellationToken));

    [HttpGet("sla-settings")]
    [Authorize(Policy = Permissions.DispatchRead)]
    public async Task<ActionResult<SlaSettingsResponse>> GetSlaSettings([FromQuery] Guid? businessId,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new GetSlaSettingsQuery(businessId), cancellationToken));

    [HttpPut("sla-settings")]
    [Authorize(Policy = Permissions.DispatchWrite)]
    public async Task<ActionResult<SlaSettingsResponse>> UpdateSlaSettings(UpdateSlaSettingsRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new UpdateSlaSettingsCommand(request.BusinessId,
            request.CourierWaitingWarningMinutes, request.CourierWaitingCriticalMinutes,
            request.PickupWarningMinutes, request.PickupCriticalMinutes,
            request.DeliveryWarningMinutes, request.DeliveryCriticalMinutes,
            request.LocationStaleWarningMinutes, request.LocationStaleCriticalMinutes), cancellationToken));
}

public sealed record UpdateSlaSettingsRequest(Guid? BusinessId,
    int CourierWaitingWarningMinutes, int CourierWaitingCriticalMinutes,
    int PickupWarningMinutes, int PickupCriticalMinutes,
    int DeliveryWarningMinutes, int DeliveryCriticalMinutes,
    int LocationStaleWarningMinutes, int LocationStaleCriticalMinutes);

using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Queries.Dispatch;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/dispatch")]
public sealed class DispatchController(ISender sender) : ApiControllerBase
{
    [HttpGet("queue")]
    [Authorize(Policy = Permissions.DispatchRead)]
    public async Task<ActionResult<PagedResponse<DispatchQueueItemResponse>>> GetQueue([FromQuery] Guid? businessId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetDispatchQueueQuery(businessId, page, pageSize), cancellationToken));

    [HttpGet("orders/{orderId:guid}/suggestions")]
    [Authorize(Policy = Permissions.DispatchRead)]
    public async Task<ActionResult<PagedResponse<CourierSuggestionResponse>>> GetSuggestions(Guid orderId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetCourierSuggestionsQuery(orderId, page, pageSize), cancellationToken));

    [HttpGet("orders/{orderId:guid}/attempts")]
    [Authorize(Policy = Permissions.DispatchRead)]
    public async Task<ActionResult<PagedResponse<DispatchAttemptResponse>>> GetAttempts(Guid orderId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetDispatchAttemptsQuery(orderId, page, pageSize), cancellationToken));

    [HttpPost("orders/{orderId:guid}/retry")]
    [Authorize(Policy = Permissions.DispatchWrite)]
    public async Task<ActionResult> Retry(Guid orderId, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new RetryDispatchCommand(orderId), cancellationToken));

    [HttpGet("settings")]
    [Authorize(Policy = Permissions.DispatchRead)]
    public async Task<ActionResult<DispatchSettingsResponse>> GetSettings([FromQuery] Guid? businessId,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new GetDispatchSettingsQuery(businessId), cancellationToken));

    [HttpPut("settings")]
    [Authorize(Policy = Permissions.DispatchWrite)]
    public async Task<ActionResult<DispatchSettingsResponse>> UpdateSettings(UpdateDispatchSettingsRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new UpdateDispatchSettingsCommand(request.BusinessId, request.AutoConfirmOrders,
            request.AutoAssignCouriers, request.AllowCourierSelfClaim, request.PreferBranchCouriers,
            request.MaxActiveOrdersPerCourier, request.RequireFreshLocation, request.LocationFreshnessMinutes,
            request.AssignmentRadiusKm, request.PreferDeliveryClusters, request.DeliveryClusterRadiusKm,
            request.DeliveryClusterMaxBearingDegrees), cancellationToken));
}

public sealed record UpdateDispatchSettingsRequest(Guid? BusinessId, bool AutoConfirmOrders, bool AutoAssignCouriers,
    bool AllowCourierSelfClaim, bool PreferBranchCouriers, int MaxActiveOrdersPerCourier,
    bool RequireFreshLocation, int LocationFreshnessMinutes, double? AssignmentRadiusKm,
    bool PreferDeliveryClusters = true, double DeliveryClusterRadiusKm = 2,
    double DeliveryClusterMaxBearingDegrees = 45);

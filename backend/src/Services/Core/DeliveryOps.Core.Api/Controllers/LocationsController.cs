using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Queries.Locations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/locations")]
public sealed class LocationsController(ISender sender) : ApiControllerBase
{
    [HttpGet("latest")]
    [Authorize(Policy = Permissions.LocationsRead)]
    public async Task<ActionResult<PagedResponse<CourierLocationSnapshot>>> Latest([FromQuery] Guid? businessId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetLatestCourierLocationsQuery(businessId, page, pageSize), cancellationToken));

    [HttpGet("couriers/{courierId:guid}/history")]
    [Authorize(Policy = Permissions.LocationsRead)]
    public async Task<ActionResult<PagedResponse<CourierLocationPoint>>> History(Guid courierId,
        [FromQuery] DateTimeOffset? fromUtc, [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetCourierLocationHistoryQuery(courierId, fromUtc, toUtc, page, pageSize), cancellationToken));

    [HttpPost("couriers/{courierId:guid}")]
    [Authorize(Policy = Permissions.LocationsWrite)]
    [EnableRateLimiting("courier-location")]
    public async Task<ActionResult<CourierLocationSnapshot>> Record(Guid courierId, RecordLocationRequest request,
        CancellationToken cancellationToken) => FromResult(await sender.Send(new RecordCourierLocationCommand(courierId,
            request.Latitude, request.Longitude, request.AccuracyMeters, request.SpeedMetersPerSecond,
            request.HeadingDegrees, request.RecordedAtUtc), cancellationToken));
}

public sealed record RecordLocationRequest(double Latitude, double Longitude, double? AccuracyMeters,
    double? SpeedMetersPerSecond, double? HeadingDegrees, DateTimeOffset? RecordedAtUtc);

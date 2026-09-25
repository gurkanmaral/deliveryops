using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Queries.Shifts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/shifts")]
[Authorize(Policy = Permissions.ShiftsManage)]
public sealed class ShiftsController(ISender sender) : ApiControllerBase
{
    [HttpGet("active")]
    public async Task<ActionResult<PagedResponse<CourierShiftResponse>>> Active([FromQuery] Guid? businessId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetActiveCourierShiftsQuery(businessId, page, pageSize), cancellationToken));

    [HttpPost("couriers/{courierId:guid}/start")]
    public async Task<ActionResult<CourierShiftResponse>> Start(Guid courierId, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new StartCourierShiftCommand(courierId), cancellationToken));

    [HttpPost("couriers/{courierId:guid}/end")]
    public async Task<ActionResult<CourierShiftResponse>> End(Guid courierId, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new EndCourierShiftCommand(courierId), cancellationToken));
}

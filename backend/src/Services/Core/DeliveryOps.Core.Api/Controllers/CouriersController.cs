using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Queries.Couriers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/couriers")]
[Authorize]
public sealed class CouriersController(ISender sender) : ApiControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<CurrentCourierResponse>> Me(CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new GetCurrentCourierQuery(), cancellationToken));

    [HttpGet("me/shift-summary")]
    public async Task<ActionResult<DeliveryOps.Core.Queries.Orders.CourierShiftSummaryResponse>> ShiftSummary(
        CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new DeliveryOps.Core.Queries.Orders.GetCourierShiftSummaryQuery(), cancellationToken));

    [HttpGet]
    [Authorize(Policy = Permissions.CouriersRead)]
    public async Task<ActionResult<PagedResponse<CourierResponse>>> GetAll(
        [FromQuery] Guid? businessId, [FromQuery] Guid? branchId, [FromQuery] bool includeInactive,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null,
        [FromQuery] string sort = "name", CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetCouriersQuery(businessId, branchId, includeInactive, page, pageSize, search, sort), cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.CouriersRead)]
    public async Task<ActionResult<CourierResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new GetCourierQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.CouriersWrite)]
    public async Task<ActionResult<CourierResponse>> Create(CreateCourierRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateCourierCommand(request.BusinessId, request.BranchId, request.FirstName, request.LastName, request.PhoneNumber), cancellationToken);
        return result.IsSuccess ? Created($"/api/v1/couriers/{result.Value!.Id}", result.Value) : FromResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.CouriersWrite)]
    public async Task<ActionResult<CourierResponse>> Update(Guid id, UpdateCourierRequest request, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new UpdateCourierCommand(id, request.FirstName, request.LastName, request.PhoneNumber), cancellationToken));

    [HttpPut("{id:guid}/assignment")]
    [Authorize(Policy = Permissions.CouriersAssign)]
    public async Task<ActionResult<CourierResponse>> Assign(Guid id, AssignCourierRequest request, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new AssignCourierToBusinessCommand(id, request.BusinessId, request.BranchId), cancellationToken));

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Permissions.CouriersWrite)]
    public async Task<ActionResult<CourierResponse>> SetStatus(Guid id, CourierStatusRequest request, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new UpdateCourierStatusCommand(id, request.Availability, request.DeliveryStatus), cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.CouriersWrite)]
    public async Task<ActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new DeactivateCourierCommand(id), cancellationToken));
}

public sealed record CreateCourierRequest(Guid BusinessId, Guid? BranchId, string FirstName, string LastName, string PhoneNumber);
public sealed record UpdateCourierRequest(string FirstName, string LastName, string PhoneNumber);
public sealed record AssignCourierRequest(Guid BusinessId, Guid? BranchId);
public sealed record CourierStatusRequest(CourierAvailability Availability, DeliveryStatus DeliveryStatus);

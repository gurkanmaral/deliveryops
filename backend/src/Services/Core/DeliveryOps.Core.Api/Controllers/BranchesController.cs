using DeliveryOps.Core.Queries.Branches;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.BuildingBlocks.Application;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/branches")]
[Authorize(Policy = Permissions.BranchesRead)]
public sealed class BranchesController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<BranchResponse>>> GetAll(
        [FromQuery] Guid? businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] string sort = "name", CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetBranchesQuery(businessId, page, pageSize, search, sort), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BranchResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new GetBranchQuery(id), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.BranchesWrite)]
    public async Task<ActionResult<BranchResponse>> Create(CreateBranchRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateBranchCommand(request.BusinessId, request.Name, request.Address, request.Latitude, request.Longitude), cancellationToken);
        return result.IsSuccess ? Created($"/api/v1/branches/{result.Value!.Id}", result.Value) : FromResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.BranchesWrite)]
    public async Task<ActionResult<BranchResponse>> Update(Guid id, UpdateBranchRequest request, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new UpdateBranchCommand(id, request.Name, request.Address, request.Latitude, request.Longitude), cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.BranchesWrite)]
    public async Task<ActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new DeactivateBranchCommand(id), cancellationToken));
}

public sealed record CreateBranchRequest(Guid BusinessId, string Name, string Address, double? Latitude, double? Longitude);
public sealed record UpdateBranchRequest(string Name, string Address, double? Latitude, double? Longitude);

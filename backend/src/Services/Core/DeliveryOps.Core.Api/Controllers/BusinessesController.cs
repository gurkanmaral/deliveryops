using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Queries.Businesses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/businesses")]
[Authorize(Policy = Permissions.BusinessesRead)]
public sealed class BusinessesController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<BusinessSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<BusinessSummary>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null,
        [FromQuery] string sort = "name", CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetBusinessesQuery(page, pageSize, search, sort), cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.BusinessesWrite)]
    [ProducesResponseType<BusinessSummary>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BusinessSummary>> Create(
        [FromBody] CreateBusinessRequest request,
        CancellationToken cancellationToken)
    {
        Result<BusinessSummary> result = await sender.Send(
            new CreateBusinessCommand(request.Name, request.Code),
            cancellationToken);

        if (result.IsFailure)
        {
            return Conflict(new ProblemDetails
            {
                Title = "İşletme oluşturulamadı",
                Detail = result.Error.Message,
                Status = StatusCodes.Status409Conflict
            });
        }

        return Created($"/api/v1/businesses/{result.Value!.Id}", result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.BusinessesWrite)]
    public async Task<ActionResult<BusinessSummary>> Update(Guid id, UpdateBusinessRequest request, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new UpdateBusinessCommand(id, request.Name), cancellationToken));

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Permissions.BusinessesWrite)]
    public async Task<ActionResult<BusinessSummary>> SetStatus(Guid id, SetBusinessStatusRequest request, CancellationToken cancellationToken) =>
        FromResult(await sender.Send(new SetBusinessActiveCommand(id, request.IsActive), cancellationToken));
}

public sealed record CreateBusinessRequest(string Name, string Code);
public sealed record UpdateBusinessRequest(string Name);
public sealed record SetBusinessStatusRequest(bool IsActive);

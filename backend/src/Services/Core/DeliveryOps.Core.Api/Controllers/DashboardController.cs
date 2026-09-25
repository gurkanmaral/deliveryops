using DeliveryOps.Core.Queries.Dashboard;
using DeliveryOps.BuildingBlocks.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Authorize(Policy = Permissions.OrdersRead)]
public sealed class DashboardController(ISender sender) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummary>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSummary>> GetSummary(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetDashboardSummaryQuery(), cancellationToken));
}

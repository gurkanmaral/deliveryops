using DeliveryOps.Core.Queries.Dashboard;
using DeliveryOps.BuildingBlocks.Security;
using MediatR;
using DeliveryOps.Core.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Authorize(Policy = CorePolicies.ManagementReports)]
public sealed class DashboardController(ISender sender) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummary>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSummary>> GetSummary(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetDashboardSummaryQuery(), cancellationToken));
}

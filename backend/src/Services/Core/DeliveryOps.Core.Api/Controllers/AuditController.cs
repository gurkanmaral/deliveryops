using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Queries.Audit;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/audit-logs")]
[Authorize(Policy = Permissions.AuditRead)]
public sealed class AuditController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<AuditLogResponse>>> Get(
        [FromQuery] Guid? businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? entityName = null, CancellationToken cancellationToken = default) =>
        FromResult(await sender.Send(new GetAuditLogsQuery(businessId, page, pageSize, entityName), cancellationToken));
}

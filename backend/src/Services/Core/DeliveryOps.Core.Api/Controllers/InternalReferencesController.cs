using DeliveryOps.Core.Api.Security;
using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/internal/references")]
[Authorize(Policy = InternalPermissions.ReferencesRead)]
public sealed class InternalReferencesController(CoreDbContext context) : ControllerBase
{
    [HttpGet("businesses/{businessId:guid}/branches/{branchId:guid}")]
    public async Task<ActionResult> ValidateBranch(Guid businessId, Guid branchId, CancellationToken cancellationToken) =>
        await context.Branches.AsNoTracking().AnyAsync(branch =>
            branch.Id == branchId && branch.BusinessId == businessId && branch.IsActive, cancellationToken)
            ? NoContent()
            : NotFound();
}

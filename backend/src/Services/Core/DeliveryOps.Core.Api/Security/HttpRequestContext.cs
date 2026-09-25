using System.Security.Claims;
using DeliveryOps.Core.Queries.Abstractions;

namespace DeliveryOps.Core.Api.Security;

public sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    private ClaimsPrincipal User => accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No active HTTP request is available.");

    public Guid UserId => ParseRequired(ClaimTypes.NameIdentifier);
    public Guid? BusinessId => ParseOptional("business_id");
    public Guid? BranchId => ParseOptional("branch_id");
    public Guid? CourierId => ParseOptional("courier_id");
    public bool IsPlatformAdmin => User.IsInRole("PlatformAdmin");

    private Guid ParseRequired(string claimType) => ParseOptional(claimType)
        ?? throw new UnauthorizedAccessException($"Required claim '{claimType}' is missing.");

    private Guid? ParseOptional(string claimType) =>
        Guid.TryParse(User.FindFirstValue(claimType), out Guid value) ? value : null;
}

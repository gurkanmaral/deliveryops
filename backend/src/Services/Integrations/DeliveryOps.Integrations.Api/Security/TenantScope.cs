using System.Security.Claims;

namespace DeliveryOps.Integrations.Api.Security;

public static class TenantScope
{
    public static Guid? GetBusinessId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("business_id")?.Value, out Guid businessId) ? businessId : null;

    public static bool CanAccess(ClaimsPrincipal user, Guid businessId)
    {
        Guid? scopedBusinessId = GetBusinessId(user);
        return !scopedBusinessId.HasValue || scopedBusinessId.Value == businessId;
    }

    public static Guid? ResolveBusinessId(ClaimsPrincipal user, Guid? requestedBusinessId)
    {
        Guid? scopedBusinessId = GetBusinessId(user);
        return scopedBusinessId ?? requestedBusinessId;
    }
}

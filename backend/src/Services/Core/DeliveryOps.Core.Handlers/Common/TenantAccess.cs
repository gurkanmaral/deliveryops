using DeliveryOps.Core.Queries.Abstractions;

namespace DeliveryOps.Core.Handlers.Common;

internal static class TenantAccess
{
    public static bool CanAccess(IRequestContext context, Guid businessId) =>
        context.IsPlatformAdmin || context.BusinessId == businessId;

    public static bool CanAccessBranch(IRequestContext context, Guid businessId, Guid branchId) =>
        CanAccess(context, businessId) &&
        (!context.BranchId.HasValue || context.BranchId.Value == branchId);

    public static bool CanAccessBranch(IRequestContext context, Guid businessId, Guid? branchId) =>
        CanAccess(context, businessId) &&
        (!context.BranchId.HasValue || context.BranchId == branchId);

    public static Guid? ResolveBusinessId(IRequestContext context, Guid? requestedBusinessId) =>
        context.IsPlatformAdmin ? requestedBusinessId : context.BusinessId;
}

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

    // Courier accounts carry a business_id claim, so the branch check alone would let one courier act on
    // another courier's shift or location. A courier may only act on their own record.
    public static bool CanManageCourier(IRequestContext context, Guid courierId, Guid businessId, Guid? branchId) =>
        context.CourierId.HasValue
            ? context.CourierId.Value == courierId
            : CanAccessBranch(context, businessId, branchId);

    public static Guid? ResolveBusinessId(IRequestContext context, Guid? requestedBusinessId) =>
        context.IsPlatformAdmin ? requestedBusinessId : context.BusinessId;
}

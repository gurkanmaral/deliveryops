using DeliveryOps.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;

namespace DeliveryOps.Core.Api.Security;

public static class CorePolicies
{
    /// <summary>
    /// Business-wide figures (dashboard, operations reports): staff with order access, never courier tokens.
    /// Couriers hold orders.read only to see their own packages.
    /// </summary>
    public const string ManagementReports = "management.reports";

    public static void AddCorePolicies(this AuthorizationOptions options) =>
        options.AddPolicy(ManagementReports, policy => policy
            .RequireClaim(Permissions.ClaimType, Permissions.OrdersRead)
            .RequireAssertion(context => !context.User.HasClaim(claim => claim.Type == "courier_id")));
}

using DeliveryOps.Auth.Api.Domain;
using DeliveryOps.BuildingBlocks.Security;

namespace DeliveryOps.Auth.Api.Security;

public static class RolePermissions
{
    public static IEnumerable<string> Resolve(IEnumerable<string> roles)
    {
        HashSet<string> values = [];
        foreach (string role in roles)
        {
            IEnumerable<string> permissions = role switch
            {
                AppRoles.PlatformAdmin => Permissions.All,
                AppRoles.BusinessAdmin =>
                [
                    Permissions.BusinessesRead, Permissions.BranchesRead, Permissions.BranchesWrite,
                    Permissions.CouriersRead, Permissions.CouriersWrite, Permissions.OrdersRead,
                    Permissions.OrdersWrite, Permissions.OrdersAssign, Permissions.OrdersTransition,
                    Permissions.LocationsRead, Permissions.ShiftsManage, Permissions.AuditRead,
                    Permissions.UsersRead, Permissions.UsersWrite, Permissions.DispatchRead, Permissions.DispatchWrite,
                    Permissions.BillingRead, Permissions.CreditsRead,
                    Permissions.IntegrationsRead, Permissions.IntegrationsWrite
                ],
                AppRoles.BusinessStaff =>
                [
                    Permissions.BusinessesRead, Permissions.BranchesRead, Permissions.CouriersRead,
                    Permissions.OrdersRead, Permissions.OrdersWrite, Permissions.OrdersAssign,
                    Permissions.OrdersTransition, Permissions.LocationsRead, Permissions.DispatchRead,
                    Permissions.BillingRead, Permissions.CreditsRead, Permissions.IntegrationsRead
                ],
                AppRoles.Courier =>
                [Permissions.OrdersRead, Permissions.OrdersClaim, Permissions.OrdersTransition, Permissions.LocationsWrite, Permissions.ShiftsManage],
                _ => []
            };
            values.UnionWith(permissions);
        }
        return values;
    }
}

using DeliveryOps.Auth.Api.Domain;
using DeliveryOps.Auth.Api.Security;
using DeliveryOps.BuildingBlocks.Security;

namespace DeliveryOps.Core.UnitTests.Security;

public sealed class AuthRolePermissionsTests
{
    [Fact]
    public void Business_staff_has_read_access_but_not_administrative_writes()
    {
        HashSet<string> permissions = RolePermissions.Resolve([AppRoles.BusinessStaff]).ToHashSet();

        Assert.Contains(Permissions.OrdersRead, permissions);
        Assert.Contains(Permissions.IntegrationsRead, permissions);
        Assert.DoesNotContain(Permissions.UsersWrite, permissions);
        Assert.DoesNotContain(Permissions.IntegrationsWrite, permissions);
        Assert.DoesNotContain(Permissions.CreditsWrite, permissions);
    }

    [Fact]
    public void Courier_permissions_are_restricted_to_delivery_workflows()
    {
        HashSet<string> permissions = RolePermissions.Resolve([AppRoles.Courier]).ToHashSet();

        Assert.Equal(5, permissions.Count);
        Assert.Contains(Permissions.OrdersClaim, permissions);
        Assert.Contains(Permissions.LocationsWrite, permissions);
        Assert.DoesNotContain(Permissions.BusinessesRead, permissions);
        Assert.DoesNotContain(Permissions.UsersRead, permissions);
    }
}

namespace DeliveryOps.BuildingBlocks.Security;

public static class Permissions
{
    public const string ClaimType = "permission";
    public const string BusinessesRead = "businesses.read";
    public const string BusinessesWrite = "businesses.write";
    public const string BranchesRead = "branches.read";
    public const string BranchesWrite = "branches.write";
    public const string CouriersRead = "couriers.read";
    public const string CouriersWrite = "couriers.write";
    public const string CouriersAssign = "couriers.assign";
    public const string OrdersRead = "orders.read";
    public const string OrdersWrite = "orders.write";
    public const string OrdersAssign = "orders.assign";
    public const string OrdersClaim = "orders.claim";
    public const string OrdersTransition = "orders.transition";
    public const string LocationsRead = "locations.read";
    public const string LocationsWrite = "locations.write";
    public const string ShiftsManage = "shifts.manage";
    public const string AuditRead = "audit.read";
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string IntegrationsRead = "integrations.read";
    public const string IntegrationsWrite = "integrations.write";
    public const string DispatchRead = "dispatch.read";
    public const string DispatchWrite = "dispatch.write";
    public const string BillingRead = "billing.read";
    public const string BillingWrite = "billing.write";
    public const string CreditsRead = "credits.read";
    public const string CreditsWrite = "credits.write";

    public static readonly string[] All =
    [
        BusinessesRead, BusinessesWrite, BranchesRead, BranchesWrite, CouriersRead, CouriersWrite,
        CouriersAssign, OrdersRead, OrdersWrite, OrdersAssign, OrdersClaim, OrdersTransition, LocationsRead,
        LocationsWrite, ShiftsManage, AuditRead, UsersRead, UsersWrite, IntegrationsRead, IntegrationsWrite,
        DispatchRead, DispatchWrite, BillingRead, BillingWrite, CreditsRead, CreditsWrite
    ];
}

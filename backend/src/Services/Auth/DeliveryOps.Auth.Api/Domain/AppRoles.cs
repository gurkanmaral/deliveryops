namespace DeliveryOps.Auth.Api.Domain;

public static class AppRoles
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string BusinessAdmin = "BusinessAdmin";
    public const string BusinessStaff = "BusinessStaff";
    public const string Courier = "Courier";

    public static readonly string[] All = [PlatformAdmin, BusinessAdmin, BusinessStaff, Courier];
}

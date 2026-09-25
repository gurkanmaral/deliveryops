namespace DeliveryOps.Notifications.Api.Domain;

public sealed class PushDevice
{
    public Guid Id { get; private init; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid CourierId { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string ExpoPushToken { get; private set; } = string.Empty;
    public string Platform { get; private set; } = string.Empty;
    public string? DeviceName { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private PushDevice() { }

    public static PushDevice Create(Guid userId, Guid courierId, Guid businessId, Guid? branchId,
        string token, string platform, string? deviceName) => new()
    {
        UserId = userId,
        CourierId = courierId,
        BusinessId = businessId,
        BranchId = branchId,
        ExpoPushToken = token.Trim(),
        Platform = platform.Trim().ToLowerInvariant(),
        DeviceName = NormalizeDeviceName(deviceName),
        IsActive = true
    };

    public void Register(Guid userId, Guid courierId, Guid businessId, Guid? branchId, string platform, string? deviceName)
    {
        UserId = userId;
        CourierId = courierId;
        BusinessId = businessId;
        BranchId = branchId;
        Platform = platform.Trim().ToLowerInvariant();
        DeviceName = NormalizeDeviceName(deviceName);
        IsActive = true;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string? NormalizeDeviceName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 120)];
}

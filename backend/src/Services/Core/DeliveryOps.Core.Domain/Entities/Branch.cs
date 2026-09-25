using DeliveryOps.BuildingBlocks.Domain;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class Branch : Entity
{
    private Branch() { }

    private Branch(Guid businessId, string name, string address, double? latitude, double? longitude)
    {
        BusinessId = businessId;
        Name = name;
        Address = address;
        Latitude = latitude;
        Longitude = longitude;
    }

    public Guid BusinessId { get; private set; }
    public Business Business { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Branch Create(Guid businessId, string name, string address, double? latitude, double? longitude)
    {
        if (businessId == Guid.Empty) throw new ArgumentException("Business is required.", nameof(businessId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return new Branch(businessId, name.Trim(), address.Trim(), latitude, longitude);
    }

    public void Update(string name, string address, double? latitude, double? longitude)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Name = name.Trim();
        Address = address.Trim();
        Latitude = latitude;
        Longitude = longitude;
        MarkAsUpdated();
    }

    public void Deactivate() { IsActive = false; MarkAsUpdated(); }
}

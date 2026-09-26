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
        ValidateCoordinates(latitude, longitude);
        return new Branch(businessId, name.Trim(), address.Trim(), latitude, longitude);
    }

    public void Update(string name, string address, double? latitude, double? longitude)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ValidateCoordinates(latitude, longitude);
        Name = name.Trim();
        Address = address.Trim();
        Latitude = latitude;
        Longitude = longitude;
        MarkAsUpdated();
    }

    private static void ValidateCoordinates(double? latitude, double? longitude)
    {
        if (latitude.HasValue != longitude.HasValue)
            throw new ArgumentException("Branch latitude and longitude must be supplied together.");
        if (!latitude.HasValue) return;
        if (!double.IsFinite(latitude.Value) || latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude));
        if (!double.IsFinite(longitude!.Value) || longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude));
        if (latitude == 0 && longitude == 0)
            throw new ArgumentException("Branch coordinates cannot be the null island coordinate.");
    }

    public void Deactivate() { IsActive = false; MarkAsUpdated(); }
}

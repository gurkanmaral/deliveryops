using DeliveryOps.BuildingBlocks.Domain;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class Courier : Entity
{
    private Courier() { }

    private Courier(Guid businessId, Guid? branchId, string firstName, string lastName, string phoneNumber)
    {
        BusinessId = businessId;
        BranchId = branchId;
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;
    }

    public Guid BusinessId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public CourierAvailability Availability { get; private set; }
    public DeliveryStatus DeliveryStatus { get; private set; }
    public DateTimeOffset? LastLocationAtUtc { get; private set; }
    public bool IsActive { get; private set; } = true;
    public uint Version { get; private set; }

    public static Courier Create(Guid businessId, Guid? branchId, string firstName, string lastName, string phoneNumber)
    {
        if (businessId == Guid.Empty) throw new ArgumentException("Business is required.", nameof(businessId));
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        return new Courier(businessId, branchId, firstName.Trim(), lastName.Trim(), phoneNumber.Trim());
    }

    public void Update(string firstName, string lastName, string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PhoneNumber = phoneNumber.Trim();
        MarkAsUpdated();
    }

    public void AssignTo(Guid businessId, Guid? branchId) { BusinessId = businessId; BranchId = branchId; MarkAsUpdated(); }
    public void SetAvailability(CourierAvailability availability) { Availability = availability; MarkAsUpdated(); }
    public void SetDeliveryStatus(DeliveryStatus status) { DeliveryStatus = status; MarkAsUpdated(); }
    public void RecordLocation(DateTimeOffset recordedAtUtc)
    {
        if (LastLocationAtUtc.HasValue && recordedAtUtc < LastLocationAtUtc.Value) return;
        LastLocationAtUtc = recordedAtUtc;
        MarkAsUpdated();
    }
    public void Deactivate() { IsActive = false; Availability = CourierAvailability.Offline; MarkAsUpdated(); }
}

using DeliveryOps.BuildingBlocks.Domain;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class OrderDispatchState : Entity
{
    private OrderDispatchState() { }

    private OrderDispatchState(Guid orderId, Guid businessId)
    {
        OrderId = orderId;
        BusinessId = businessId;
        Status = DispatchStatus.Pending;
        NextAttemptAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid OrderId { get; private init; }
    public Guid BusinessId { get; private init; }
    public DispatchStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? LastAttemptAtUtc { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public string? LastReason { get; private set; }
    public Guid? AssignedCourierId { get; private set; }

    public static OrderDispatchState Create(Guid orderId, Guid businessId) => new(orderId, businessId);

    public void Queue(DateTimeOffset now)
    {
        Status = DispatchStatus.Pending;
        NextAttemptAtUtc = now;
        LastReason = null;
        AssignedCourierId = null;
        MarkAsUpdated();
    }

    public void MarkNoCourier(DateTimeOffset now, string reason)
    {
        AttemptCount++;
        Status = DispatchStatus.NoEligibleCourier;
        LastAttemptAtUtc = now;
        NextAttemptAtUtc = now.AddSeconds(Math.Min(300, 15 * Math.Pow(2, Math.Min(AttemptCount - 1, 5))));
        LastReason = reason;
        AssignedCourierId = null;
        MarkAsUpdated();
    }

    public void MarkAssigned(DateTimeOffset now, Guid courierId)
    {
        AttemptCount++;
        Status = DispatchStatus.Assigned;
        LastAttemptAtUtc = now;
        NextAttemptAtUtc = null;
        LastReason = null;
        AssignedCourierId = courierId;
        MarkAsUpdated();
    }

    public void MarkDisabled(DateTimeOffset now)
    {
        Status = DispatchStatus.Disabled;
        LastAttemptAtUtc = now;
        NextAttemptAtUtc = null;
        LastReason = "Otomatik kurye atama kapalı.";
        MarkAsUpdated();
    }
}

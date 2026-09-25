using DeliveryOps.BuildingBlocks.Domain;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class DispatchAttempt : Entity
{
    private DispatchAttempt() { }

    public Guid OrderId { get; private init; }
    public Guid BusinessId { get; private init; }
    public Guid? CourierId { get; private init; }
    public string Trigger { get; private init; } = string.Empty;
    public bool WasSuccessful { get; private init; }
    public string? Reason { get; private init; }

    public static DispatchAttempt Create(Guid orderId, Guid businessId, Guid? courierId,
        string trigger, bool wasSuccessful, string? reason) => new()
    {
        OrderId = orderId,
        BusinessId = businessId,
        CourierId = courierId,
        Trigger = trigger,
        WasSuccessful = wasSuccessful,
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
    };
}

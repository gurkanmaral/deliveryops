using DeliveryOps.BuildingBlocks.Domain;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class CourierShift : Entity
{
    private CourierShift() { }
    public Guid CourierId { get; private init; }
    public Guid BusinessId { get; private init; }
    public DateTimeOffset StartedAtUtc { get; private init; }
    public DateTimeOffset? EndedAtUtc { get; private set; }

    public static CourierShift Start(Guid courierId, Guid businessId, DateTimeOffset now) =>
        new() { CourierId = courierId, BusinessId = businessId, StartedAtUtc = now };

    public void End(DateTimeOffset now)
    {
        if (EndedAtUtc.HasValue) throw new InvalidOperationException("Shift has already ended.");
        EndedAtUtc = now;
        MarkAsUpdated();
    }
}

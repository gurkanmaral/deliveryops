namespace DeliveryOps.BuildingBlocks.Domain;

public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; protected init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; protected set; }

    protected void MarkAsUpdated() => UpdatedAtUtc = DateTimeOffset.UtcNow;
}

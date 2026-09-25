namespace DeliveryOps.Notifications.Api.Domain;

public sealed class NotificationReceipt
{
    public Guid Id { get; private init; } = Guid.NewGuid();
    public Guid EventId { get; private init; }
    public string EventType { get; private init; } = string.Empty;
    public int TargetCount { get; private init; }
    public DateTimeOffset ProcessedAtUtc { get; private init; } = DateTimeOffset.UtcNow;

    private NotificationReceipt() { }
    public static NotificationReceipt Create(Guid eventId, string eventType, int targetCount) =>
        new() { EventId = eventId, EventType = eventType, TargetCount = targetCount };
}

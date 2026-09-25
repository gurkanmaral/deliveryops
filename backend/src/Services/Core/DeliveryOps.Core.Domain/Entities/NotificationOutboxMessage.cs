using DeliveryOps.BuildingBlocks.Domain;
using DeliveryOps.Core.Domain.Enums;
using System.Text.Json;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class NotificationOutboxMessage : Entity
{
    private NotificationOutboxMessage() { }
    public string EventType { get; private init; } = string.Empty;
    public string PayloadJson { get; private init; } = "{}";
    public Guid BusinessId { get; private init; }
    public Guid OrderId { get; private init; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessingAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public DateTimeOffset? DeadLetteredAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public int? LastHttpStatusCode { get; private set; }

    public static NotificationOutboxMessage CreateForOrder(Order order)
    {
        if (order.Status is not (OrderStatus.WaitingForCourier or OrderStatus.Assigned))
            throw new InvalidOperationException("Only available or assigned orders can produce a notification event.");
        Guid eventId = Guid.NewGuid();
        string eventType = order.Status == OrderStatus.Assigned ? "OrderAssigned" : "OrderAvailable";
        string payload = JsonSerializer.Serialize(new
        {
            EventId = eventId,
            EventType = eventType,
            OrderId = order.Id,
            order.BusinessId,
            order.BranchId,
            CourierId = order.Status == OrderStatus.Assigned ? order.CourierId : null
        });
        return new NotificationOutboxMessage
        {
            Id = eventId,
            EventType = eventType,
            PayloadJson = payload,
            BusinessId = order.BusinessId,
            OrderId = order.Id
        };
    }

    public void MarkProcessing(DateTimeOffset now) => ProcessingAtUtc = now;

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAtUtc = now;
        ProcessingAtUtc = null;
        LastError = null;
        LastHttpStatusCode = null;
    }

    public void MarkFailed(string error, DateTimeOffset now, int maxAttempts,
        int? httpStatusCode = null, bool permanent = false)
    {
        Attempts++;
        ProcessingAtUtc = null;
        LastError = error[..Math.Min(error.Length, 1000)];
        LastHttpStatusCode = httpStatusCode;
        if (permanent || Attempts >= maxAttempts)
        {
            DeadLetteredAtUtc = now;
            return;
        }
        NextAttemptAtUtc = now.AddSeconds(Math.Min(Math.Pow(2, Attempts), 300));
    }

    public void ReleaseForShutdown(DateTimeOffset now)
    {
        ProcessingAtUtc = null;
        NextAttemptAtUtc = now;
    }

    public void Retry(DateTimeOffset now)
    {
        if (ProcessedAtUtc.HasValue)
            throw new InvalidOperationException("Processed outbox messages cannot be retried.");
        Attempts = 0;
        ProcessingAtUtc = null;
        DeadLetteredAtUtc = null;
        LastError = null;
        LastHttpStatusCode = null;
        NextAttemptAtUtc = now;
    }
}

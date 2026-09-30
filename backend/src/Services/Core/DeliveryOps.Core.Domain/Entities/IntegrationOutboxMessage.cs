using System.Text.Json;
using DeliveryOps.BuildingBlocks.Domain;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class IntegrationOutboxMessage : Entity
{
    private IntegrationOutboxMessage() { }

    public string EventType { get; private init; } = "OrderStatusChanged";
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

    public static IntegrationOutboxMessage CreateForOrder(Order order)
    {
        if (order.Source is not (OrderSource.Yemeksepeti or OrderSource.Getir))
            throw new InvalidOperationException("Only Yemeksepeti and Getir orders produce provider status events.");

        Guid eventId = Guid.NewGuid();
        return new IntegrationOutboxMessage
        {
            Id = eventId,
            BusinessId = order.BusinessId,
            OrderId = order.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                EventId = eventId,
                OrderId = order.Id,
                order.BusinessId,
                order.BranchId,
                order.ExternalId,
                Source = order.Source,
                Status = order.Status,
                order.DeliveryFulfillment,
                order.CancellationReason,
                OccurredAtUtc = DateTimeOffset.UtcNow
            })
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

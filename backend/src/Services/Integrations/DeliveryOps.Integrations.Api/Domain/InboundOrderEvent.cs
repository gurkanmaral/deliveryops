namespace DeliveryOps.Integrations.Api.Domain;

public enum InboundEventStatus { Received, Processing, Completed, Failed, DeadLettered }

public sealed class InboundOrderEvent
{
    private InboundOrderEvent() { }

    public Guid Id { get; private set; }
    public Guid ConnectionId { get; private set; }
    public IntegrationConnection Connection { get; private set; } = null!;
    public string ExternalEventId { get; private set; } = string.Empty;
    public string ExternalOrderId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = "order.created";
    public string AdapterVersion { get; private set; } = "canonical-v1";
    public string PayloadHash { get; private set; } = string.Empty;
    public string RawPayload { get; private set; } = string.Empty;
    public string NormalizedPayload { get; private set; } = string.Empty;
    public InboundEventStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public Guid? CoreOrderId { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset? LastAttemptAtUtc { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public static InboundOrderEvent Receive(Guid connectionId, string externalEventId, string externalOrderId,
        string eventType, string adapterVersion, string payloadHash, string rawPayload, string normalizedPayload) => new()
    {
        Id = Guid.NewGuid(), ConnectionId = connectionId, ExternalEventId = externalEventId.Trim(),
        ExternalOrderId = externalOrderId.Trim(), EventType = eventType.Trim(), AdapterVersion = adapterVersion.Trim(),
        PayloadHash = payloadHash, RawPayload = rawPayload, NormalizedPayload = normalizedPayload,
        Status = InboundEventStatus.Received, ReceivedAtUtc = DateTimeOffset.UtcNow,
        NextAttemptAtUtc = DateTimeOffset.UtcNow
    };

    public void StartProcessing(DateTimeOffset now) { Status = InboundEventStatus.Processing; Attempts++; LastAttemptAtUtc = now; NextAttemptAtUtc = null; LastError = null; }
    public void Complete(Guid coreOrderId, DateTimeOffset now) { Status = InboundEventStatus.Completed; CoreOrderId = coreOrderId; ProcessedAtUtc = now; NextAttemptAtUtc = null; LastError = null; }
    public void Fail(string error, DateTimeOffset now, int maxAttempts)
    {
        LastError = error[..Math.Min(error.Length, 1000)];
        if (Attempts >= maxAttempts) { Status = InboundEventStatus.DeadLettered; NextAttemptAtUtc = null; return; }
        Status = InboundEventStatus.Failed;
        NextAttemptAtUtc = now.AddSeconds(Math.Min(300, 15 * Math.Pow(2, Math.Max(0, Attempts - 1))));
    }

    public void Retry(DateTimeOffset now)
    {
        if (Status == InboundEventStatus.Completed) throw new InvalidOperationException("Tamamlanmış olay yeniden işlenemez.");
        Status = InboundEventStatus.Received;
        NextAttemptAtUtc = now;
        LastError = null;
    }
}

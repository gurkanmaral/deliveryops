namespace DeliveryOps.Integrations.Api.Domain;

public enum OutboundEventStatus { Pending, Processing, Completed, Failed, DeadLettered }

public sealed class OutboundOrderEvent
{
    private OutboundOrderEvent() { }

    public Guid Id { get; private set; }
    public Guid SourceEventId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public IntegrationConnection Connection { get; private set; } = null!;
    public Guid CoreOrderId { get; private set; }
    public string ExternalOrderId { get; private set; } = string.Empty;
    public string ProviderStatus { get; private set; } = string.Empty;
    public string? CancellationReason { get; private set; }
    public OutboundEventStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? LastAttemptAtUtc { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public static OutboundOrderEvent Create(Guid sourceEventId, Guid connectionId, Guid coreOrderId,
        string externalOrderId, string providerStatus, string? cancellationReason, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), SourceEventId = sourceEventId, ConnectionId = connectionId,
        CoreOrderId = coreOrderId, ExternalOrderId = externalOrderId.Trim(),
        ProviderStatus = providerStatus.Trim(), CancellationReason = cancellationReason?.Trim(),
        Status = OutboundEventStatus.Pending, CreatedAtUtc = now, NextAttemptAtUtc = now
    };

    public void StartProcessing(DateTimeOffset now)
    {
        Status = OutboundEventStatus.Processing;
        Attempts++;
        LastAttemptAtUtc = now;
        NextAttemptAtUtc = null;
        LastError = null;
    }

    public void Complete(DateTimeOffset now)
    {
        Status = OutboundEventStatus.Completed;
        ProcessedAtUtc = now;
        NextAttemptAtUtc = null;
        LastError = null;
    }

    public void Fail(string error, DateTimeOffset now, int maxAttempts)
    {
        LastError = error[..Math.Min(error.Length, 1000)];
        if (Attempts >= maxAttempts)
        {
            Status = OutboundEventStatus.DeadLettered;
            NextAttemptAtUtc = null;
            return;
        }
        Status = OutboundEventStatus.Failed;
        NextAttemptAtUtc = now.AddSeconds(Math.Min(300, 15 * Math.Pow(2, Math.Max(0, Attempts - 1))));
    }

    public void Retry(DateTimeOffset now)
    {
        if (Status == OutboundEventStatus.Completed)
            throw new InvalidOperationException("Tamamlanmış olay yeniden işlenemez.");
        Status = OutboundEventStatus.Pending;
        NextAttemptAtUtc = now;
        LastError = null;
    }
}

using DeliveryOps.BuildingBlocks.Domain;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class OperationalAlert : Entity
{
    private OperationalAlert() { }

    public Guid BusinessId { get; private init; }
    public Guid? OrderId { get; private init; }
    public Guid? CourierId { get; private init; }
    public string AlertKey { get; private init; } = string.Empty;
    public OperationalAlertType Type { get; private init; }
    public OperationalAlertSeverity Severity { get; private set; }
    public OperationalAlertStatus Status { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public DateTimeOffset FirstDetectedAtUtc { get; private init; }
    public DateTimeOffset LastChangedAtUtc { get; private set; }
    public Guid? AcknowledgedByUserId { get; private set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public static OperationalAlert Create(Guid businessId, Guid? orderId, Guid? courierId, string alertKey,
        OperationalAlertType type, OperationalAlertSeverity severity, string title, string message, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alertKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new OperationalAlert
        {
            BusinessId = businessId, OrderId = orderId, CourierId = courierId, AlertKey = alertKey,
            Type = type, Severity = severity, Status = OperationalAlertStatus.Active, Title = title.Trim(),
            Message = message.Trim(), FirstDetectedAtUtc = now, LastChangedAtUtc = now
        };
    }

    public bool Refresh(OperationalAlertSeverity severity, string title, string message, DateTimeOffset now)
    {
        bool changed = Status == OperationalAlertStatus.Resolved || Severity != severity || Title != title || Message != message;
        if (!changed) return false;
        Severity = severity;
        Title = title.Trim();
        Message = message.Trim();
        if (Status == OperationalAlertStatus.Resolved)
        {
            Status = OperationalAlertStatus.Active;
            ResolvedAtUtc = null;
            AcknowledgedAtUtc = null;
            AcknowledgedByUserId = null;
        }
        LastChangedAtUtc = now;
        MarkAsUpdated();
        return true;
    }

    public void Acknowledge(Guid userId, DateTimeOffset now)
    {
        if (Status == OperationalAlertStatus.Resolved) throw new InvalidOperationException("Çözülmüş uyarı onaylanamaz.");
        Status = OperationalAlertStatus.Acknowledged;
        AcknowledgedByUserId = userId;
        AcknowledgedAtUtc = now;
        LastChangedAtUtc = now;
        MarkAsUpdated();
    }

    public bool Resolve(DateTimeOffset now)
    {
        if (Status == OperationalAlertStatus.Resolved) return false;
        Status = OperationalAlertStatus.Resolved;
        ResolvedAtUtc = now;
        LastChangedAtUtc = now;
        MarkAsUpdated();
        return true;
    }
}

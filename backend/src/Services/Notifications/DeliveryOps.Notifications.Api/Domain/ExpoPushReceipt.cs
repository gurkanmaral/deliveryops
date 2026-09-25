namespace DeliveryOps.Notifications.Api.Domain;

public sealed class ExpoPushReceipt
{
    public Guid Id { get; private init; } = Guid.NewGuid();
    public string TicketId { get; private init; } = string.Empty;
    public string ExpoPushToken { get; private init; } = string.Empty;
    public int Attempts { get; private set; }
    public DateTimeOffset NextCheckAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private init; } = DateTimeOffset.UtcNow;

    private ExpoPushReceipt() { }
    public static ExpoPushReceipt Create(string ticketId, string token, DateTimeOffset now) => new()
    {
        TicketId = ticketId,
        ExpoPushToken = token,
        NextCheckAtUtc = now.AddMinutes(15)
    };

    public void Complete(string? error, DateTimeOffset now)
    {
        CompletedAtUtc = now;
        Error = error;
    }

    public void Retry(DateTimeOffset now)
    {
        Attempts++;
        NextCheckAtUtc = now.AddMinutes(Math.Min(15 * Math.Pow(2, Attempts), 360));
    }
}

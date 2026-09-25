namespace DeliveryOps.Integrations.Api.Domain;

public sealed class IntegrationHealthCheck
{
    private IntegrationHealthCheck() { }

    public Guid Id { get; private set; }
    public Guid ConnectionId { get; private set; }
    public IntegrationConnection Connection { get; private set; } = null!;
    public bool Succeeded { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public ProviderEnvironment Environment { get; private set; }
    public bool Automatic { get; private set; }
    public DateTimeOffset CheckedAtUtc { get; private set; }
    public DateTimeOffset? TokenExpiresAtUtc { get; private set; }

    public static IntegrationHealthCheck Create(Guid connectionId, bool succeeded, string message,
        ProviderEnvironment environment, bool automatic, DateTimeOffset checkedAtUtc,
        DateTimeOffset? tokenExpiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        string trimmedMessage = message.Trim();
        return new IntegrationHealthCheck
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            Succeeded = succeeded,
            Message = trimmedMessage[..Math.Min(trimmedMessage.Length, 500)],
            Environment = environment,
            Automatic = automatic,
            CheckedAtUtc = checkedAtUtc,
            TokenExpiresAtUtc = succeeded ? tokenExpiresAtUtc : null
        };
    }
}

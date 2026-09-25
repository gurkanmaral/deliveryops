namespace DeliveryOps.Integrations.Api.Domain;

public sealed class IntegrationConnection
{
    private IntegrationConnection() { }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid BranchId { get; private set; }
    public IntegrationProvider Provider { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string SecretHash { get; private set; } = string.Empty;
    public string? ProtectedSecret { get; private set; }
    public string? PreviousSecretHash { get; private set; }
    public string? PreviousProtectedSecret { get; private set; }
    public DateTimeOffset? PreviousSecretValidUntilUtc { get; private set; }
    public WebhookAuthMode AuthMode { get; private set; }
    public string AdapterVersion { get; private set; } = "canonical-v1";
    public string? ProtectedCredentials { get; private set; }
    public string? ProviderAccountId { get; private set; }
    public ProviderEnvironment ProviderEnvironment { get; private set; } = ProviderEnvironment.Sandbox;
    public bool CredentialsConfigured => !string.IsNullOrWhiteSpace(ProtectedCredentials) &&
                                         !string.IsNullOrWhiteSpace(ProviderAccountId);
    public DateTimeOffset? LastHealthCheckAtUtc { get; private set; }
    public bool? LastHealthCheckSucceeded { get; private set; }
    public string? LastHealthCheckMessage { get; private set; }
    public DateTimeOffset? LastTokenExpiresAtUtc { get; private set; }
    public int ConsecutiveHealthCheckFailures { get; private set; }
    public DateTimeOffset? LastAutomaticHealthCheckAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static IntegrationConnection Create(Guid businessId, Guid branchId, IntegrationProvider provider,
        string name, string secretHash, string protectedSecret, WebhookAuthMode authMode,
        string adapterVersion = "canonical-v1") => new()
    {
        Id = Guid.NewGuid(), BusinessId = businessId, BranchId = branchId, Provider = provider,
        Name = name.Trim(), SecretHash = secretHash, ProtectedSecret = protectedSecret,
        AuthMode = authMode, AdapterVersion = adapterVersion.Trim(), IsActive = true,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    public void SetActive(bool isActive)
    {
        if (isActive && !IsActive) LastAutomaticHealthCheckAtUtc = null;
        IsActive = isActive;
    }
    public void RotateSecret(string secretHash, string protectedSecret, DateTimeOffset previousSecretValidUntilUtc)
    {
        if (previousSecretValidUntilUtc == default)
            throw new ArgumentOutOfRangeException(nameof(previousSecretValidUntilUtc));
        PreviousSecretHash = SecretHash;
        PreviousProtectedSecret = ProtectedSecret;
        PreviousSecretValidUntilUtc = previousSecretValidUntilUtc;
        SecretHash = secretHash;
        ProtectedSecret = protectedSecret;
    }


    public void ConfigureProvider(string providerAccountId, string protectedCredentials,
        ProviderEnvironment environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedCredentials);
        ProviderAccountId = providerAccountId.Trim();
        ProtectedCredentials = protectedCredentials;
        ProviderEnvironment = environment;
        LastHealthCheckAtUtc = null;
        LastHealthCheckSucceeded = null;
        LastHealthCheckMessage = null;
        LastTokenExpiresAtUtc = null;
        ConsecutiveHealthCheckFailures = 0;
        LastAutomaticHealthCheckAtUtc = null;
    }

    public void RecordHealthCheck(bool succeeded, string message, DateTimeOffset checkedAtUtc,
        DateTimeOffset? tokenExpiresAtUtc, bool automatic = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        string trimmedMessage = message.Trim();
        LastHealthCheckAtUtc = checkedAtUtc;
        LastHealthCheckSucceeded = succeeded;
        LastHealthCheckMessage = trimmedMessage[..Math.Min(trimmedMessage.Length, 500)];
        LastTokenExpiresAtUtc = succeeded ? tokenExpiresAtUtc : null;
        ConsecutiveHealthCheckFailures = succeeded ? 0 : ConsecutiveHealthCheckFailures + 1;
        if (automatic) LastAutomaticHealthCheckAtUtc = checkedAtUtc;
    }
}

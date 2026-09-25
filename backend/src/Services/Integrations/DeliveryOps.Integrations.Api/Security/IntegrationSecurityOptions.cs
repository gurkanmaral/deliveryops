namespace DeliveryOps.Integrations.Api.Security;

public sealed class IntegrationSecurityOptions
{
    public const string SectionName = "IntegrationSecurity";
    public int WebhookPreviousSecretGraceMinutes { get; init; } = 1440;
    public int WebhookRateLimitPerMinute { get; init; } = 300;
}

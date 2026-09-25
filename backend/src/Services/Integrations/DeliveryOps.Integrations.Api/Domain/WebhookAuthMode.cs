namespace DeliveryOps.Integrations.Api.Domain;

public enum WebhookAuthMode
{
    ApiKey = 0,
    HmacSha256 = 1,
    StaticAuthorization = 2
}

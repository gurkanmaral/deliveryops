using Microsoft.AspNetCore.DataProtection;

namespace DeliveryOps.Integrations.Api.Security;

public sealed class WebhookSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("DeliveryOps.Integrations.WebhookSecret.v1");

    public string Protect(string secret) => _protector.Protect(secret);
    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}

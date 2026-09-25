using System.Security.Claims;
using System.Text.Encodings.Web;
using DeliveryOps.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DeliveryOps.Core.Api.Security;

public sealed class InternalApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "InternalApiKey";
    private const string HeaderName = "X-DeliveryOps-Internal-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var suppliedValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        string supplied = suppliedValues.ToString();
        if (!ApiKeyValidator.IsValid(supplied, configuration["Integrations:InternalApiKey"],
                configuration.GetSection("Integrations:PreviousInternalApiKeys").Get<string[]>() ?? []))
            return Task.FromResult(AuthenticateResult.Fail("Invalid internal API key."));

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000001"),
            new(ClaimTypes.Name, "Integrations Service"),
            new(Permissions.ClaimType, InternalPermissions.OrdersIngest),
            new(Permissions.ClaimType, InternalPermissions.ReferencesRead),
            new(Permissions.ClaimType, InternalPermissions.OperationalAlertsWrite)
        ];
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

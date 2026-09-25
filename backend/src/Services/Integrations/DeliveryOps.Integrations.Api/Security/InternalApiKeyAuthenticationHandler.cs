using System.Security.Claims;
using System.Text.Encodings.Web;
using DeliveryOps.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DeliveryOps.Integrations.Api.Security;

public sealed class InternalApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "InternalApiKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-DeliveryOps-Internal-Key", out var suppliedValues))
            return Task.FromResult(AuthenticateResult.NoResult());
        if (!ApiKeyValidator.IsValid(suppliedValues.ToString(), configuration["InternalApiKey"],
                configuration.GetSection("PreviousInternalApiKeys").Get<string[]>() ?? []))
            return Task.FromResult(AuthenticateResult.Fail("Invalid internal API key."));
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, "core-service"),
            new(ClaimTypes.Name, "Core Service"),
            new(Permissions.ClaimType, InternalPermissions.OrderStatusWrite)
        ];
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

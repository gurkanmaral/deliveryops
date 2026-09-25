namespace DeliveryOps.Auth.Api.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string PrivateKeyPem { get; init; } = string.Empty;
    public string PrivateKeyPemBase64 { get; init; } = string.Empty;
    public string PublicKeyPem { get; init; } = string.Empty;
    public string PublicKeyPemBase64 { get; init; } = string.Empty;
    public string[] PreviousPublicKeysPem { get; init; } = [];
    public string[] PreviousPublicKeysPemBase64 { get; init; } = [];
    public string SigningKey { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 14;
}

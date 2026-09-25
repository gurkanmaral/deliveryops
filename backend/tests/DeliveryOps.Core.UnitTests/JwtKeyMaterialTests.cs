using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using DeliveryOps.BuildingBlocks.Security;
using Microsoft.IdentityModel.Tokens;

namespace DeliveryOps.Core.UnitTests;

public sealed class JwtKeyMaterialTests
{
    [Fact]
    public void Api_key_rotation_accepts_active_and_previous_keys_only()
    {
        Assert.True(ApiKeyValidator.IsValid("active", "active", ["previous"]));
        Assert.True(ApiKeyValidator.IsValid("previous", "active", ["previous"]));
        Assert.False(ApiKeyValidator.IsValid("unknown", "active", ["previous"]));
        Assert.False(ApiKeyValidator.IsValid(string.Empty, "active", ["previous"]));
    }

    [Fact]
    public void Rsa_private_key_signs_tokens_that_public_key_validates()
    {
        using RSA rsa = RSA.Create(2048);
        string privatePem = rsa.ExportPkcs8PrivateKeyPem();
        string publicPem = rsa.ExportSubjectPublicKeyInfoPem();
        SecurityKey signingKey = JwtKeyMaterial.CreateSigningKey(privatePem, null, null, false);
        SecurityKey validationKey = JwtKeyMaterial.CreateValidationKey(publicPem, null, null, null, null, false);
        JwtSecurityToken token = new(
            issuer: "issuer",
            audience: "audience",
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(signingKey, JwtKeyMaterial.AlgorithmFor(signingKey)));

        ClaimsPrincipal principal = new JwtSecurityTokenHandler().ValidateToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "issuer",
                ValidateAudience = true,
                ValidAudience = "audience",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = validationKey,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
            },
            out SecurityToken validatedToken);

        Assert.NotNull(principal);
        Assert.IsType<JwtSecurityToken>(validatedToken);
    }

    [Fact]
    public void Symmetric_fallback_is_rejected_outside_development()
    {
        Assert.Throws<InvalidOperationException>(() =>
            JwtKeyMaterial.CreateValidationKey(null, null, null, null, new string('x', 32), false));
    }

    [Fact]
    public void Active_and_previous_public_keys_are_accepted_during_rotation()
    {
        using RSA activeRsa = RSA.Create(2048);
        using RSA previousRsa = RSA.Create(2048);
        SecurityKey activeSigningKey = JwtKeyMaterial.CreateSigningKey(
            activeRsa.ExportPkcs8PrivateKeyPem(), null, null, false);
        SecurityKey previousSigningKey = JwtKeyMaterial.CreateSigningKey(
            previousRsa.ExportPkcs8PrivateKeyPem(), null, null, false);
        IReadOnlyList<SecurityKey> validationKeys = JwtKeyMaterial.CreateValidationKeys(
            activeRsa.ExportSubjectPublicKeyInfoPem(), null, null, null,
            [previousRsa.ExportSubjectPublicKeyInfoPem()], null, null, false);

        Assert.Equal(2, validationKeys.Count);
        Validate(CreateToken(activeSigningKey), validationKeys);
        Validate(CreateToken(previousSigningKey), validationKeys);
    }

    private static string CreateToken(SecurityKey key)
    {
        JwtSecurityToken token = new(
            issuer: "issuer", audience: "audience", expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, JwtKeyMaterial.AlgorithmFor(key)));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void Validate(string token, IEnumerable<SecurityKey> keys)
    {
        ClaimsPrincipal principal = new JwtSecurityTokenHandler().ValidateToken(token,
            new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "issuer",
                ValidateAudience = true, ValidAudience = "audience",
                ValidateLifetime = true, ValidateIssuerSigningKey = true,
                IssuerSigningKeys = keys,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
            }, out _);
        Assert.NotNull(principal);
    }
}

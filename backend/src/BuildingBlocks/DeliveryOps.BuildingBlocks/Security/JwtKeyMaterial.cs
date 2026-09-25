using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace DeliveryOps.BuildingBlocks.Security;

public static class JwtKeyMaterial
{
    public static SecurityKey CreateSigningKey(
        string? privateKeyPem,
        string? privateKeyPemBase64,
        string? developmentSigningKey,
        bool allowDevelopmentSymmetricKey)
    {
        string? pem = ReadPem(privateKeyPem, privateKeyPemBase64);
        if (!string.IsNullOrWhiteSpace(pem))
        {
            RSA rsa = RSA.Create();
            try
            {
                rsa.ImportFromPem(pem);
                return CreateRsaKey(rsa, includePrivateParameters: true);
            }
            catch
            {
                rsa.Dispose();
                throw;
            }
        }

        return CreateDevelopmentSymmetricKey(developmentSigningKey, allowDevelopmentSymmetricKey);
    }

    public static SecurityKey CreateValidationKey(
        string? publicKeyPem,
        string? publicKeyPemBase64,
        string? privateKeyPem,
        string? privateKeyPemBase64,
        string? developmentSigningKey,
        bool allowDevelopmentSymmetricKey)
        => CreateValidationKeys(publicKeyPem, publicKeyPemBase64, privateKeyPem, privateKeyPemBase64,
            null, null, developmentSigningKey, allowDevelopmentSymmetricKey)[0];

    public static IReadOnlyList<SecurityKey> CreateValidationKeys(
        string? publicKeyPem,
        string? publicKeyPemBase64,
        string? privateKeyPem,
        string? privateKeyPemBase64,
        IEnumerable<string>? previousPublicKeysPem,
        IEnumerable<string>? previousPublicKeysPemBase64,
        string? developmentSigningKey,
        bool allowDevelopmentSymmetricKey)
    {
        List<SecurityKey> keys = [];
        AddRsaValidationKey(keys, ReadPem(publicKeyPem, publicKeyPemBase64) ??
                                  ReadPem(privateKeyPem, privateKeyPemBase64));
        foreach (string pem in previousPublicKeysPem ?? [])
            AddRsaValidationKey(keys, ReadPem(pem, null));
        foreach (string pemBase64 in previousPublicKeysPemBase64 ?? [])
            AddRsaValidationKey(keys, ReadPem(null, pemBase64));

        SecurityKey[] distinctKeys = keys.GroupBy(x => x.KeyId, StringComparer.Ordinal).Select(x => x.First()).ToArray();
        if (distinctKeys.Length > 0) return distinctKeys;
        return [CreateDevelopmentSymmetricKey(developmentSigningKey, allowDevelopmentSymmetricKey)];
    }

    public static string AlgorithmFor(SecurityKey key) =>
        key is RsaSecurityKey ? SecurityAlgorithms.RsaSha256 : SecurityAlgorithms.HmacSha256;

    private static RsaSecurityKey CreateRsaKey(RSA rsa, bool includePrivateParameters)
    {
        RSAParameters parameters = rsa.ExportParameters(includePrivateParameters);
        RsaSecurityKey key = new(parameters);
        byte[] publicKey = rsa.ExportSubjectPublicKeyInfo();
        key.KeyId = Convert.ToHexString(SHA256.HashData(publicKey))[..16];
        rsa.Dispose();
        return key;
    }

    private static void AddRsaValidationKey(ICollection<SecurityKey> keys, string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem)) return;
        RSA rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            keys.Add(CreateRsaKey(rsa, includePrivateParameters: false));
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    private static SymmetricSecurityKey CreateDevelopmentSymmetricKey(string? signingKey, bool allowed)
    {
        if (!allowed)
            throw new InvalidOperationException(
                "JWT RSA key material is missing. Configure Jwt:PrivateKeyPem on Auth and Jwt:PublicKeyPem on resource APIs.");
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException("The development JWT signing key must contain at least 32 bytes.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
    }

    private static string? ReadPem(string? pem, string? pemBase64)
    {
        if (!string.IsNullOrWhiteSpace(pem)) return pem.Replace("\\n", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(pemBase64)) return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(pemBase64));
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("JWT PEM base64 configuration is invalid.", exception);
        }
    }
}

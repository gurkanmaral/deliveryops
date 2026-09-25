using System.Security.Cryptography;
using System.Text;

namespace DeliveryOps.Integrations.Api.Security;

public static class SecretHasher
{
    public static string CreateSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static bool Verify(string value, string expectedHash)
    {
        byte[] actual = Convert.FromHexString(Hash(value));
        byte[] expected = Convert.FromHexString(expectedHash);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

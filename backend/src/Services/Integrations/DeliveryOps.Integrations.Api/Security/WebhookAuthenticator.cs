using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DeliveryOps.Integrations.Api.Domain;

namespace DeliveryOps.Integrations.Api.Security;

public sealed class WebhookAuthenticator(WebhookSecretProtector secretProtector, TimeProvider timeProvider)
{
    public bool Verify(HttpRequest request, IntegrationConnection connection, string rawPayload)
    {
        if (connection.AuthMode == WebhookAuthMode.ApiKey)
            return request.Headers.TryGetValue("X-DeliveryOps-Key", out var suppliedSecret) && VerifyHash(
                suppliedSecret.ToString(), connection, timeProvider.GetUtcNow());
        if (connection.AuthMode == WebhookAuthMode.StaticAuthorization)
            return request.Headers.TryGetValue("Authorization", out var authorization) && VerifyHash(
                authorization.ToString(), connection, timeProvider.GetUtcNow());
        if (connection.ProtectedSecret is null ||
            !request.Headers.TryGetValue("X-DeliveryOps-Timestamp", out var timestampHeader) ||
            !request.Headers.TryGetValue("X-DeliveryOps-Signature", out var signatureHeader) ||
            !long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out long unixSeconds))
            return false;
        DateTimeOffset timestamp;
        try { timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
        catch (ArgumentOutOfRangeException) { return false; }
        if (Math.Abs((timeProvider.GetUtcNow() - timestamp).TotalMinutes) > 5) return false;
        string signature = signatureHeader.ToString();
        if (signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) signature = signature[7..];
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature); }
        catch (FormatException) { return false; }
        byte[] signedPayload = Encoding.UTF8.GetBytes($"{unixSeconds}.{rawPayload}");
        bool valid = VerifyHmac(supplied, connection.ProtectedSecret, signedPayload);
        if (connection.PreviousSecretValidUntilUtc > timeProvider.GetUtcNow())
            valid |= VerifyHmac(supplied, connection.PreviousProtectedSecret, signedPayload);
        return valid;
    }

    private static bool VerifyHash(string suppliedSecret, IntegrationConnection connection, DateTimeOffset now)
    {
        bool activeValid = SecretHasher.Verify(suppliedSecret, connection.SecretHash);
        bool previousValid = connection.PreviousSecretValidUntilUtc > now &&
                             !string.IsNullOrWhiteSpace(connection.PreviousSecretHash) &&
                             SecretHasher.Verify(suppliedSecret, connection.PreviousSecretHash);
        return activeValid | previousValid;
    }

    private bool VerifyHmac(byte[] supplied, string? protectedSecret, byte[] signedPayload)
    {
        if (string.IsNullOrWhiteSpace(protectedSecret)) return false;
        string secret;
        try { secret = secretProtector.Unprotect(protectedSecret); }
        catch (CryptographicException) { return false; }
        byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signedPayload);
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }
}

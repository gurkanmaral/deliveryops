using System.Security.Cryptography;
using System.Text;

namespace DeliveryOps.BuildingBlocks.Security;

public static class ApiKeyValidator
{
    public static bool IsValid(string? supplied, string? activeKey, IEnumerable<string>? previousKeys = null)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        bool valid = FixedTimeEquals(activeKey, supplied);
        foreach (string previousKey in previousKeys ?? [])
            valid |= FixedTimeEquals(previousKey, supplied);
        return valid;
    }

    private static bool FixedTimeEquals(string? expected, string supplied)
    {
        if (string.IsNullOrWhiteSpace(expected)) return false;
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}

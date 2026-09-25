using System.Security.Cryptography;
using System.Text;

namespace DeliveryOps.PosBridge.Wpf.Services;

public sealed class WindowsSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DeliveryOps.PosBridge.v1");

    public string Protect(string value) => Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));

    public string Unprotect(string protectedValue) => Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser));
}

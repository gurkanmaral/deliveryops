namespace DeliveryOps.PosBridge.Wpf.Services;

public interface ISecretProtector
{
    string Protect(string value);
    string Unprotect(string protectedValue);
}

namespace DeliveryOps.Core.Queries.Abstractions;

public interface IOrderPiiProtector
{
    string Protect(string value);
    string Unprotect(string value);
    string BuildSearchTokens(string customerName, string customerPhone);
    string CreateSearchToken(string search);
}

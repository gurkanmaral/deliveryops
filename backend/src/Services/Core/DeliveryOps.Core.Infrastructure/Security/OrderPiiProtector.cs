using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DeliveryOps.Core.Queries.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace DeliveryOps.Core.Infrastructure.Security;

public sealed class OrderPiiProtector : IOrderPiiProtector
{
    private const string CipherPrefix = "enc:v1:";
    private readonly IDataProtector _protector;
    private readonly byte[] _searchKey;

    public OrderPiiProtector(IDataProtectionProvider provider, IOptions<OrderPiiOptions> options)
    {
        _protector = provider.CreateProtector("DeliveryOps.Core.OrderPii.v1");
        string searchKey = options.Value.SearchKey;
        if (Encoding.UTF8.GetByteCount(searchKey) < 32)
            throw new InvalidOperationException("OrderPii:SearchKey must contain at least 32 UTF-8 bytes.");
        _searchKey = Encoding.UTF8.GetBytes(searchKey);
    }

    public string Protect(string value) => value.StartsWith(CipherPrefix, StringComparison.Ordinal)
        ? value
        : CipherPrefix + _protector.Protect(value);

    public string Unprotect(string value) => value.StartsWith(CipherPrefix, StringComparison.Ordinal)
        ? _protector.Unprotect(value[CipherPrefix.Length..])
        : value;

    public string BuildSearchTokens(string customerName, string customerPhone)
    {
        HashSet<string> candidates = new(StringComparer.Ordinal);
        string normalizedName = NormalizeName(customerName);
        AddPrefixes(candidates, "n:", normalizedName, 2);
        foreach (string word in normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            AddPrefixes(candidates, "n:", word, 2);

        string digits = DigitsOnly(customerPhone);
        AddPrefixes(candidates, "p:", digits, 3);
        for (int length = 4; length <= digits.Length; length++)
            candidates.Add("p:" + digits[^length..]);

        return string.Join('|', candidates.Select(Hash).Order(StringComparer.Ordinal));
    }

    public string CreateSearchToken(string search)
    {
        string digits = DigitsOnly(search);
        bool looksLikePhone = digits.Length >= 3 && search.All(character => char.IsDigit(character) || char.IsWhiteSpace(character) || "+()-./".Contains(character));
        return Hash(looksLikePhone ? "p:" + digits : "n:" + NormalizeName(search));
    }

    private string Hash(string value)
    {
        byte[] hash = HMACSHA256.HashData(_searchKey, Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }

    private static void AddPrefixes(ISet<string> values, string prefix, string value, int minimumLength)
    {
        for (int length = minimumLength; length <= value.Length; length++)
            values.Add(prefix + value[..length]);
    }

    private static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

    private static string NormalizeName(string value)
    {
        string normalized = value.Trim().Normalize(NormalizationForm.FormKC).ToUpper(CultureInfo.GetCultureInfo("tr-TR"));
        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

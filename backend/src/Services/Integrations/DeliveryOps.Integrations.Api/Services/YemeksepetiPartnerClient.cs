using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Security;

namespace DeliveryOps.Integrations.Api.Services;

public sealed class YemeksepetiPartnerClient(IHttpClientFactory httpClientFactory,
    WebhookSecretProtector protector, TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<Guid, CachedToken> _tokens = new();

    public async Task UpdateOrderStatusAsync(IntegrationConnection connection, string orderId,
        string providerStatus, string? cancellationReason, string rawInboundPayload,
        CancellationToken cancellationToken)
    {
        if (!connection.CredentialsConfigured || connection.ProtectedCredentials is null ||
            connection.ProviderAccountId is null)
            throw new InvalidOperationException("Yemeksepeti OAuth bilgileri henüz yapılandırılmadı.");

        JsonNode inbound = JsonNode.Parse(rawInboundPayload)
            ?? throw new InvalidOperationException("Orijinal Yemeksepeti siparişi okunamadı.");
        JsonNode? items = inbound["items"]?.DeepClone();
        if (items is not JsonArray)
            throw new InvalidOperationException("Yemeksepeti durum güncellemesi için orijinal items alanı bulunamadı.");

        CachedToken token = await GetTokenAsync(connection, cancellationToken);
        JsonObject payload = new()
        {
            ["order_id"] = orderId,
            ["status"] = providerStatus,
            ["items"] = items
        };
        if (providerStatus == "CANCELLED")
            payload["cancellation"] = new JsonObject
            {
                ["reason"] = string.IsNullOrWhiteSpace(cancellationReason) ? "OTHER" : cancellationReason
            };

        string baseUrl = connection.ProviderEnvironment == ProviderEnvironment.Production
            ? "https://yemeksepeti.partner.deliveryhero.io/"
            : "https://sandbox.partner.deliveryhero.io/";
        using HttpRequestMessage request = new(HttpMethod.Put,
            $"{baseUrl}v2/chains/{Uri.EscapeDataString(connection.ProviderAccountId)}/orders/{Uri.EscapeDataString(orderId)}")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using HttpResponseMessage response = await httpClientFactory.CreateClient("YemeksepetiPartner")
            .SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;

        string error = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            _tokens.TryRemove(connection.Id, out _);
        throw new HttpRequestException(
            $"Yemeksepeti status update failed ({(int)response.StatusCode}): {error[..Math.Min(error.Length, 800)]}",
            null, response.StatusCode);
    }

    public async Task<YemeksepetiConnectionTestResult> TestConnectionAsync(
        IntegrationConnection connection, CancellationToken cancellationToken)
    {
        ValidateConfiguration(connection);
        Invalidate(connection.Id);
        CachedToken token = await GetTokenAsync(connection, cancellationToken);
        return new YemeksepetiConnectionTestResult(connection.ProviderEnvironment, token.ExpiresAtUtc);
    }

    public void Invalidate(Guid connectionId) => _tokens.TryRemove(connectionId, out _);

    private async Task<CachedToken> GetTokenAsync(IntegrationConnection connection,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration(connection);
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (_tokens.TryGetValue(connection.Id, out CachedToken? cached) && cached.ExpiresAtUtc > now.AddMinutes(1))
            return cached;

        YemeksepetiCredentials credentials = JsonSerializer.Deserialize<YemeksepetiCredentials>(
            protector.Unprotect(connection.ProtectedCredentials!))
            ?? throw new InvalidOperationException("Yemeksepeti OAuth bilgileri okunamadı.");
        string baseUrl = connection.ProviderEnvironment == ProviderEnvironment.Production
            ? "https://yemeksepeti.partner.deliveryhero.io/"
            : "https://sandbox.partner.deliveryhero.io/";
        using FormUrlEncodedContent content = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret
        });
        using HttpResponseMessage response = await httpClientFactory.CreateClient("YemeksepetiPartner")
            .PostAsync($"{baseUrl}v2/oauth/token", content, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Yemeksepeti OAuth failed ({(int)response.StatusCode}): {body[..Math.Min(body.Length, 800)]}",
                null, response.StatusCode);
        OAuthTokenResponse token = JsonSerializer.Deserialize<OAuthTokenResponse>(body)
            ?? throw new InvalidOperationException("Yemeksepeti OAuth yanıtı okunamadı.");
        if (string.IsNullOrWhiteSpace(token.AccessToken) || token.ExpiresIn <= 0)
            throw new InvalidOperationException("Yemeksepeti OAuth yanıtı geçerli bir token içermiyor.");
        CachedToken result = new(token.AccessToken, now.AddSeconds(Math.Max(60, token.ExpiresIn)));
        _tokens[connection.Id] = result;
        return result;
    }

    private static void ValidateConfiguration(IntegrationConnection connection)
    {
        if (connection.Provider != IntegrationProvider.Yemeksepeti)
            throw new InvalidOperationException("Bu bağlantı Yemeksepeti bağlantısı değil.");
        if (!connection.CredentialsConfigured || connection.ProtectedCredentials is null ||
            connection.ProviderAccountId is null)
            throw new InvalidOperationException("Yemeksepeti OAuth bilgileri henüz yapılandırılmadı.");
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
    private sealed record OAuthTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

public sealed record YemeksepetiCredentials(string ClientId, string ClientSecret);
public sealed record YemeksepetiConnectionTestResult(ProviderEnvironment Environment,
    DateTimeOffset TokenExpiresAtUtc);

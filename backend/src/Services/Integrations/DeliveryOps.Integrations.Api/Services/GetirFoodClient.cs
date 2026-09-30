using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Security;

namespace DeliveryOps.Integrations.Api.Services;

/// <summary>
/// GetirFood restaurant API (developers.getir.com, Swagger 1.5.8). Orders must be verified within
/// 30 seconds of arriving; afterwards prepare → handover (Getir courier) or deliver (own courier).
/// The API has no idempotency keys, so every transition that fails is reconciled by reading the order
/// and checking whether the step's timestamp is already set.
/// </summary>
public sealed class GetirFoodClient(IHttpClientFactory httpClientFactory, WebhookSecretProtector protector,
    TimeProvider timeProvider, ILogger<GetirFoodClient> logger)
{
    public const string HttpClientName = "GetirFood";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, CachedToken> _tokens = new();

    public static Uri BaseUrl(ProviderEnvironment environment) => environment == ProviderEnvironment.Production
        ? new Uri("https://food-external-api-gateway.getirapi.com/")
        : new Uri("https://food-external-api-gateway.development.getirapi.com/");

    /// <summary>Logs in with the keys Getir issued and returns the restaurant id they belong to.</summary>
    public async Task<GetirLoginResult> LoginAsync(ProviderEnvironment environment, GetirCredentials credentials,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Client().PostAsJsonAsync(
            new Uri(BaseUrl(environment), "auth/login"),
            new { appSecretKey = credentials.AppSecretKey, restaurantSecretKey = credentials.RestaurantSecretKey },
            cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Getir login failed ({(int)response.StatusCode}): {Describe(body)}",
                null, response.StatusCode);
        LoginResponse login = JsonSerializer.Deserialize<LoginResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Getir login yanıtı okunamadı.");
        if (string.IsNullOrWhiteSpace(login.Token) || string.IsNullOrWhiteSpace(login.RestaurantId))
            throw new InvalidOperationException("Getir login yanıtı token veya restoran kimliği içermiyor.");
        // Tokens are valid for one hour and cannot be refreshed; renew a few minutes early.
        return new GetirLoginResult(login.RestaurantId, login.Token, timeProvider.GetUtcNow().AddMinutes(55));
    }

    public async Task<GetirLoginResult> TestConnectionAsync(IntegrationConnection connection,
        CancellationToken cancellationToken)
    {
        Invalidate(connection.Id);
        GetirLoginResult login = await LoginAsync(connection.ProviderEnvironment, ReadCredentials(connection),
            cancellationToken);
        _tokens[connection.Id] = new CachedToken(login.Token, login.ExpiresAtUtc);
        return login;
    }

    public void Invalidate(Guid connectionId) => _tokens.TryRemove(connectionId, out _);

    public Task VerifyAsync(IntegrationConnection connection, string foodOrderId, bool scheduled,
        CancellationToken cancellationToken) =>
        TransitionAsync(connection, foodOrderId, scheduled ? "verify-scheduled" : "verify",
            order => order.VerifyDate is not null || order.ScheduleVerifiedDate is not null,
            alreadyDoneErrorCode: 2, cancellationToken);

    public Task PrepareAsync(IntegrationConnection connection, string foodOrderId,
        CancellationToken cancellationToken) =>
        TransitionAsync(connection, foodOrderId, "prepare", order => order.PrepareDate is not null,
            alreadyDoneErrorCode: null, cancellationToken);

    public Task HandoverAsync(IntegrationConnection connection, string foodOrderId,
        CancellationToken cancellationToken) =>
        TransitionAsync(connection, foodOrderId, "handover", order => order.HandoverDate is not null,
            alreadyDoneErrorCode: null, cancellationToken);

    public Task DeliverAsync(IntegrationConnection connection, string foodOrderId,
        CancellationToken cancellationToken) =>
        TransitionAsync(connection, foodOrderId, "deliver", order => order.DeliverDate is not null,
            alreadyDoneErrorCode: null, cancellationToken);

    public async Task<GetirOrderState> GetOrderAsync(IntegrationConnection connection, string foodOrderId,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(connection, HttpMethod.Get,
            $"food-orders/{Uri.EscapeDataString(foodOrderId)}", cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Getir order read failed ({(int)response.StatusCode}): {Describe(body)}",
                null, response.StatusCode);
        return JsonSerializer.Deserialize<GetirOrderState>(body, JsonOptions)
               ?? throw new InvalidOperationException("Getir sipariş yanıtı okunamadı.");
    }

    private async Task TransitionAsync(IntegrationConnection connection, string foodOrderId, string action,
        Func<GetirOrderState, bool> alreadyDone, int? alreadyDoneErrorCode, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(connection, HttpMethod.Post,
            $"food-orders/{Uri.EscapeDataString(foodOrderId)}/{action}", cancellationToken);
        if (response.IsSuccessStatusCode) return;
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        int? errorCode = ReadErrorCode(body);
        if (alreadyDoneErrorCode.HasValue && errorCode == alreadyDoneErrorCode) return;
        if (errorCode == 13) throw new GetirOrderCancelledException(foodOrderId);
        // A retry of a step that already went through returns a status error; confirm from the order itself.
        GetirOrderState? order = null;
        try { order = await GetOrderAsync(connection, foodOrderId, cancellationToken); }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not reconcile Getir order {OrderId} after {Action} failed.", foodOrderId, action);
        }
        if (order is not null && alreadyDone(order)) return;
        throw new HttpRequestException($"Getir {action} failed ({(int)response.StatusCode}): {Describe(body)}",
            null, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(IntegrationConnection connection, HttpMethod method,
        string path, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            string token = await GetTokenAsync(connection, cancellationToken);
            HttpRequestMessage request = new(method, new Uri(BaseUrl(connection.ProviderEnvironment), path));
            request.Headers.Add("token", token);
            HttpResponseMessage response = await Client().SendAsync(request, cancellationToken);
            request.Dispose();
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt > 0) return response;
            // The cached token expired or was revoked; log in again once.
            response.Dispose();
            Invalidate(connection.Id);
        }
    }

    private async Task<string> GetTokenAsync(IntegrationConnection connection, CancellationToken cancellationToken)
    {
        if (_tokens.TryGetValue(connection.Id, out CachedToken? cached) && cached.ExpiresAtUtc > timeProvider.GetUtcNow())
            return cached.Token;
        GetirLoginResult login = await LoginAsync(connection.ProviderEnvironment, ReadCredentials(connection),
            cancellationToken);
        _tokens[connection.Id] = new CachedToken(login.Token, login.ExpiresAtUtc);
        return login.Token;
    }

    private GetirCredentials ReadCredentials(IntegrationConnection connection)
    {
        if (connection.Provider != IntegrationProvider.Getir)
            throw new InvalidOperationException("Bu bağlantı Getir bağlantısı değil.");
        if (!connection.CredentialsConfigured || connection.ProtectedCredentials is null)
            throw new InvalidOperationException("Getir anahtarları henüz yapılandırılmadı.");
        return JsonSerializer.Deserialize<GetirCredentials>(protector.Unprotect(connection.ProtectedCredentials))
               ?? throw new InvalidOperationException("Getir anahtarları okunamadı.");
    }

    private HttpClient Client() => httpClientFactory.CreateClient(HttpClientName);

    private static int? ReadErrorCode(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("code", out JsonElement code) && code.TryGetInt32(out int value)
                ? value : null;
        }
        catch (JsonException) { return null; }
    }

    private static string Describe(string body) => body.Length <= 500 ? body : body[..500];

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAtUtc);
    private sealed record LoginResponse(string? RestaurantId, string? Token);
}

public sealed record GetirCredentials(string AppSecretKey, string RestaurantSecretKey);
public sealed record GetirLoginResult(string RestaurantId, string Token, DateTimeOffset ExpiresAtUtc);

public sealed record GetirOrderState(
    string? Id,
    int? Status,
    [property: JsonPropertyName("verifyDate")] string? VerifyDate,
    [property: JsonPropertyName("scheduleVerifiedDate")] string? ScheduleVerifiedDate,
    [property: JsonPropertyName("prepareDate")] string? PrepareDate,
    [property: JsonPropertyName("handoverDate")] string? HandoverDate,
    [property: JsonPropertyName("deliverDate")] string? DeliverDate,
    [property: JsonPropertyName("deliveryType")] int? DeliveryType);

public sealed class GetirOrderCancelledException(string foodOrderId)
    : InvalidOperationException($"Getir siparişi {foodOrderId} Getir tarafında iptal edilmiş.");

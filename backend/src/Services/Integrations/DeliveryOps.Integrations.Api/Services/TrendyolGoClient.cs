using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Security;

namespace DeliveryOps.Integrations.Api.Services;

/// <summary>
/// Uber Eats Trendyol Go – Yemek integrator API (developers.tgoapps.com). Every request carries Basic auth
/// (API key / API secret), a "{supplierId} - {integrator}" User-Agent and the x-agentname / x-executor-user
/// headers. Meal orders have no webhook: packages are polled with GET /packages and moved forward with
/// picked (accept) → invoiced (ready) → manual-shipped → manual-delivered (own courier only) or
/// unsupplied (restaurant cancellation). Failed steps are reconciled by reading the package status.
/// </summary>
public sealed class TrendyolGoClient(IHttpClientFactory httpClientFactory, WebhookSecretProtector protector,
    IConfiguration configuration, ILogger<TrendyolGoClient> logger)
{
    public const string HttpClientName = "TrendyolGo";
    public const int MaxPageSize = 50;
    private static readonly string[] StatusOrder = ["Created", "Picking", "Invoiced", "Shipped", "Delivered"];

    public static Uri BaseUrl(ProviderEnvironment environment) => environment == ProviderEnvironment.Production
        ? new Uri("https://api.tgoapis.com/integrator/")
        : new Uri("https://stageapi.tgoapis.com/integrator/");

    /// <summary>Integrator name used in User-Agent and x-agentname; "SelfIntegration" unless configured.</summary>
    private string AgentName => configuration["Trendyol:AgentName"] is { Length: > 0 } name ? name : "SelfIntegration";

    /// <summary>Reads one page of packages; proves the keys and supplier/store ids are accepted.</summary>
    public async Task<TrendyolPackagePage> GetPackagesAsync(ProviderEnvironment environment, string supplierId,
        TrendyolCredentials credentials, long? modifiedFromMs, long? modifiedToMs, int page, int size,
        CancellationToken cancellationToken)
    {
        StringBuilder query = new($"order/meal/suppliers/{Uri.EscapeDataString(supplierId)}/packages?page={page}&size={size}");
        if (!string.IsNullOrWhiteSpace(credentials.StoreId))
            query.Append("&storeId=").Append(Uri.EscapeDataString(credentials.StoreId));
        if (modifiedFromMs.HasValue) query.Append("&packageModificationStartDate=").Append(modifiedFromMs.Value);
        if (modifiedToMs.HasValue) query.Append("&packageModificationEndDate=").Append(modifiedToMs.Value);
        using HttpResponseMessage response = await SendAsync(environment, supplierId, credentials, HttpMethod.Get,
            query.ToString(), null, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Trendyol packages read failed ({(int)response.StatusCode}): {Describe(body)}",
                null, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        List<string> packages = [];
        if (root.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
            packages.AddRange(content.EnumerateArray().Select(item => item.GetRawText()));
        int totalPages = root.TryGetProperty("totalPages", out JsonElement pages) && pages.TryGetInt32(out int count)
            ? count : 1;
        return new TrendyolPackagePage(packages, totalPages);
    }

    public Task<TrendyolPackagePage> GetPackagesAsync(IntegrationConnection connection, long? modifiedFromMs,
        long? modifiedToMs, int page, CancellationToken cancellationToken) =>
        GetPackagesAsync(connection.ProviderEnvironment, SupplierId(connection), ReadCredentials(connection),
            modifiedFromMs, modifiedToMs, page, MaxPageSize, cancellationToken);

    public async Task TestConnectionAsync(IntegrationConnection connection, CancellationToken cancellationToken) =>
        await GetPackagesAsync(connection.ProviderEnvironment, SupplierId(connection), ReadCredentials(connection),
            null, null, 0, 1, cancellationToken);

    public async Task<string?> GetPackageStatusAsync(IntegrationConnection connection, string packageId,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(connection, HttpMethod.Get,
            $"order/meal/suppliers/{Uri.EscapeDataString(SupplierId(connection))}/packages/{Uri.EscapeDataString(packageId)}",
            null, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Trendyol package read failed ({(int)response.StatusCode}): {Describe(body)}",
                null, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement package = document.RootElement;
        if (package.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
        {
            if (content.GetArrayLength() == 0) return null;
            package = content[0];
        }
        return package.TryGetProperty("packageStatus", out JsonElement status) && status.ValueKind == JsonValueKind.String
            ? status.GetString() : null;
    }

    /// <summary>Accepts the order ("Picking").</summary>
    public Task AcceptAsync(IntegrationConnection connection, string packageId, int preparationMinutes,
        CancellationToken cancellationToken) =>
        TransitionAsync(connection, packageId, "packages/picked",
            new { packageId, preparationTime = preparationMinutes }, "Picking", cancellationToken);

    /// <summary>Preparation finished ("Invoiced"). Uber stores may already be invoiced automatically.</summary>
    public Task InvoiceAsync(IntegrationConnection connection, string packageId, CancellationToken cancellationToken) =>
        TransitionAsync(connection, packageId, "packages/invoiced", new { packageId }, "Invoiced", cancellationToken);

    /// <summary>Own courier left with the order.</summary>
    public Task ShipAsync(IntegrationConnection connection, string packageId, CancellationToken cancellationToken) =>
        TransitionAsync(connection, packageId, $"packages/{Uri.EscapeDataString(packageId)}/manual-shipped",
            new { }, "Shipped", cancellationToken);

    /// <summary>Own courier (or the counter, for pickup orders) delivered the order.</summary>
    public Task DeliverAsync(IntegrationConnection connection, string packageId, CancellationToken cancellationToken) =>
        TransitionAsync(connection, packageId, $"packages/{Uri.EscapeDataString(packageId)}/manual-delivered",
            new { }, "Delivered", cancellationToken);

    /// <summary>Restaurant-side cancellation of every item in the package.</summary>
    public async Task UnsupplyAsync(IntegrationConnection connection, string packageId, IReadOnlyList<string> itemIds,
        int reasonId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(connection, HttpMethod.Put,
            $"order/meal/suppliers/{Uri.EscapeDataString(SupplierId(connection))}/packages/unsupplied",
            new { packageId, itemIdList = itemIds, reasonId }, cancellationToken);
        if (response.IsSuccessStatusCode) return;
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string? status = await TryReadStatusAsync(connection, packageId, cancellationToken);
        if (status is "UnSupplied" or "Cancelled") return;
        throw new HttpRequestException($"Trendyol unsupplied failed ({(int)response.StatusCode}): {Describe(body)}",
            null, response.StatusCode);
    }

    private async Task TransitionAsync(IntegrationConnection connection, string packageId, string path, object body,
        string targetStatus, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(connection, HttpMethod.Put,
            $"order/meal/suppliers/{Uri.EscapeDataString(SupplierId(connection))}/{path}", body, cancellationToken);
        if (response.IsSuccessStatusCode) return;
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        // A retried step usually fails because the package already moved on; confirm from the package itself.
        string? status = await TryReadStatusAsync(connection, packageId, cancellationToken);
        if (status is "Cancelled" or "UnSupplied") throw new TrendyolPackageCancelledException(packageId);
        if (status is not null && Array.IndexOf(StatusOrder, status) >= Array.IndexOf(StatusOrder, targetStatus)) return;
        throw new HttpRequestException(
            $"Trendyol {targetStatus} failed ({(int)response.StatusCode}): {Describe(responseBody)}", null, response.StatusCode);
    }

    private async Task<string?> TryReadStatusAsync(IntegrationConnection connection, string packageId,
        CancellationToken cancellationToken)
    {
        try { return await GetPackageStatusAsync(connection, packageId, cancellationToken); }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not read Trendyol package {PackageId} status.", packageId);
            return null;
        }
    }

    private Task<HttpResponseMessage> SendAsync(IntegrationConnection connection, HttpMethod method, string path,
        object? body, CancellationToken cancellationToken) =>
        SendAsync(connection.ProviderEnvironment, SupplierId(connection), ReadCredentials(connection), method, path,
            body, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(ProviderEnvironment environment, string supplierId,
        TrendyolCredentials credentials, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, new Uri(BaseUrl(environment), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.ApiKey}:{credentials.ApiSecret}")));
        request.Headers.TryAddWithoutValidation("User-Agent", $"{supplierId} - {AgentName}");
        request.Headers.TryAddWithoutValidation("x-agentname", AgentName);
        request.Headers.TryAddWithoutValidation("x-executor-user", credentials.ExecutorEmail);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
    }

    private static string SupplierId(IntegrationConnection connection) =>
        connection.ProviderAccountId ?? throw new InvalidOperationException("Trendyol satıcı ID henüz yapılandırılmadı.");

    private TrendyolCredentials ReadCredentials(IntegrationConnection connection)
    {
        if (connection.Provider != IntegrationProvider.Trendyol)
            throw new InvalidOperationException("Bu bağlantı Trendyol bağlantısı değil.");
        if (!connection.CredentialsConfigured || connection.ProtectedCredentials is null)
            throw new InvalidOperationException("Trendyol API bilgileri henüz yapılandırılmadı.");
        return JsonSerializer.Deserialize<TrendyolCredentials>(protector.Unprotect(connection.ProtectedCredentials))
               ?? throw new InvalidOperationException("Trendyol API bilgileri okunamadı.");
    }

    private static string Describe(string body) => body.Length <= 500 ? body : body[..500];
}

public sealed record TrendyolCredentials(string ApiKey, string ApiSecret, string StoreId, string ExecutorEmail);
public sealed record TrendyolPackagePage(IReadOnlyList<string> Packages, int TotalPages);

public sealed class TrendyolPackageCancelledException(string packageId)
    : InvalidOperationException($"Trendyol paketi {packageId} Trendyol tarafında iptal edilmiş.");

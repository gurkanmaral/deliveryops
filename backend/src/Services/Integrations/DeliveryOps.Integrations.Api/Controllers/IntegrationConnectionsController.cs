using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using DeliveryOps.Integrations.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryOps.Integrations.Api.Services;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DeliveryOps.Integrations.Api.Controllers;

[ApiController]
[Route("api/v1/integrations")]
[Authorize]
public sealed class IntegrationConnectionsController(IntegrationsDbContext context, CoreOrdersClient coreOrdersClient,
    WebhookSecretProtector secretProtector, ProviderAdapterRegistry adapterRegistry,
    YemeksepetiPartnerClient yemeksepetiPartnerClient, GetirFoodClient getirFoodClient,
    TrendyolGoClient trendyolClient,
    IntegrationConnectionHealthChecker healthChecker, TimeProvider timeProvider,
    IOptions<IntegrationSecurityOptions> securityOptions,
    ILogger<IntegrationConnectionsController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.IntegrationsRead)]
    public async Task<ActionResult<PagedResponse<IntegrationConnectionResponse>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        IQueryable<IntegrationConnection> query = context.Connections.AsNoTracking();
        Guid? scopedBusinessId = TenantScope.GetBusinessId(User);
        if (scopedBusinessId.HasValue) query = query.Where(item => item.BusinessId == scopedBusinessId.Value);
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        int totalCount = await query.CountAsync(cancellationToken);
        List<IntegrationConnectionResponse> items = await query.OrderBy(item => item.Name)
            .ThenBy(item => item.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(item =>
            new IntegrationConnectionResponse(item.Id, item.BusinessId, item.BranchId, item.Provider, item.Name,
                item.AuthMode, item.AdapterVersion, item.IsActive, item.CreatedAtUtc,
                $"/api/v1/webhooks/{item.Id}/orders", null, item.CredentialsConfigured,
                item.ProviderAccountId, item.ProviderEnvironment, item.LastHealthCheckAtUtc,
                item.LastHealthCheckSucceeded, item.LastHealthCheckMessage,
                item.LastTokenExpiresAtUtc, item.ConsecutiveHealthCheckFailures,
                item.LastAutomaticHealthCheckAtUtc, item.PreviousSecretValidUntilUtc)).ToListAsync(cancellationToken);
        return Ok(new PagedResponse<IntegrationConnectionResponse>(items, page, pageSize, totalCount));
    }

    [HttpGet("events")]
    [Authorize(Policy = Permissions.IntegrationsRead)]
    public async Task<ActionResult<PagedResponse<InboundEventResponse>>> GetEvents(
        [FromQuery] Guid? businessId,
        [FromQuery] Guid? connectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        Guid? scopedBusinessId = TenantScope.GetBusinessId(User);
        if (scopedBusinessId.HasValue && businessId.HasValue && businessId != scopedBusinessId) return Forbid();
        businessId = scopedBusinessId ?? businessId;
        var query = context.InboundOrderEvents.AsNoTracking().AsQueryable();
        if (businessId.HasValue) query = query.Where(item => item.Connection.BusinessId == businessId);
        if (connectionId.HasValue) query = query.Where(item => item.ConnectionId == connectionId);
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        int totalCount = await query.CountAsync(cancellationToken);
        List<InboundEventResponse> items = await query.OrderByDescending(item => item.ReceivedAtUtc)
            .ThenByDescending(item => item.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new InboundEventResponse(item.Id, item.ConnectionId, item.ExternalEventId,
                item.ExternalOrderId, item.EventType, item.AdapterVersion, item.Status,
                item.Attempts, item.CoreOrderId, item.LastError, item.ReceivedAtUtc,
                item.NextAttemptAtUtc, item.ProcessedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(new PagedResponse<InboundEventResponse>(items, page, pageSize, totalCount));
    }

    [HttpGet("outbound-events")]
    [Authorize(Policy = Permissions.IntegrationsRead)]
    public async Task<ActionResult<PagedResponse<OutboundEventResponse>>> GetOutboundEvents(
        [FromQuery] Guid? businessId, [FromQuery] Guid? connectionId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        Guid? scopedBusinessId = TenantScope.GetBusinessId(User);
        if (scopedBusinessId.HasValue && businessId.HasValue && businessId != scopedBusinessId) return Forbid();
        businessId = scopedBusinessId ?? businessId;
        var query = context.OutboundOrderEvents.AsNoTracking().AsQueryable();
        if (businessId.HasValue) query = query.Where(x => x.Connection.BusinessId == businessId.Value);
        if (connectionId.HasValue) query = query.Where(x => x.ConnectionId == connectionId.Value);
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        int totalCount = await query.CountAsync(cancellationToken);
        List<OutboundEventResponse> items = await query.OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new OutboundEventResponse(x.Id, x.ConnectionId, x.CoreOrderId,
                x.ExternalOrderId, x.ProviderStatus, x.Status, x.Attempts, x.LastError,
                x.CreatedAtUtc, x.NextAttemptAtUtc, x.ProcessedAtUtc)).ToListAsync(cancellationToken);
        return Ok(new PagedResponse<OutboundEventResponse>(items, page, pageSize, totalCount));
    }

    [HttpPut("{id:guid}/yemeksepeti-credentials")]
    [Authorize(Policy = Permissions.IntegrationsWrite)]
    public async Task<ActionResult<IntegrationConnectionResponse>> ConfigureYemeksepeti(Guid id,
        ConfigureYemeksepetiRequest request, CancellationToken cancellationToken)
    {
        IntegrationConnection? connection = await context.Connections.FindAsync([id], cancellationToken);
        if (connection is null) return NotFound();
        if (!TenantScope.CanAccess(User, connection.BusinessId)) return Forbid();
        if (connection.Provider != IntegrationProvider.Yemeksepeti)
            return BadRequest(new ProblemDetails { Title = "Bu bağlantı Yemeksepeti bağlantısı değil.", Status = 400 });
        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ClientSecret) ||
            string.IsNullOrWhiteSpace(request.ChainId))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                { ["credentials"] = ["Client ID, client secret ve chain ID zorunludur."] }));
        string protectedCredentials = secretProtector.Protect(JsonSerializer.Serialize(
            new YemeksepetiCredentials(request.ClientId.Trim(), request.ClientSecret.Trim())));
        connection.ConfigureProvider(request.ChainId, protectedCredentials, request.Environment);
        yemeksepetiPartnerClient.Invalidate(connection.Id);
        await context.SaveChangesAsync(cancellationToken);
        return Ok(Map(connection));
    }

    [HttpPut("{id:guid}/getir-credentials")]
    [Authorize(Policy = Permissions.IntegrationsWrite)]
    public async Task<ActionResult<IntegrationConnectionResponse>> ConfigureGetir(Guid id,
        ConfigureGetirRequest request, CancellationToken cancellationToken)
    {
        IntegrationConnection? connection = await context.Connections.FindAsync([id], cancellationToken);
        if (connection is null) return NotFound();
        if (!TenantScope.CanAccess(User, connection.BusinessId)) return Forbid();
        if (connection.Provider != IntegrationProvider.Getir)
            return BadRequest(new ProblemDetails { Title = "Bu bağlantı Getir bağlantısı değil.", Status = 400 });
        if (string.IsNullOrWhiteSpace(request.AppSecretKey) || string.IsNullOrWhiteSpace(request.RestaurantSecretKey))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                { ["credentials"] = ["App secret key ve restaurant secret key zorunludur."] }));
        GetirCredentials credentials = new(request.AppSecretKey.Trim(), request.RestaurantSecretKey.Trim());
        GetirLoginResult login;
        // Log in before saving: it proves the keys work and returns the Getir restaurant they belong to.
        try { login = await getirFoodClient.LoginAsync(request.Environment, credentials, cancellationToken); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Getir login failed while configuring connection {ConnectionId}.", id);
            return BadRequest(new ProblemDetails
            {
                Title = "Getir anahtarları doğrulanamadı.",
                Detail = exception is HttpRequestException { StatusCode: not null }
                    ? "Getir anahtarları kabul etmedi. Ortamı (test/canlı) ve anahtarları kontrol edin."
                    : "Getir servisine ulaşılamadı. Daha sonra tekrar deneyin.",
                Status = 400
            });
        }
        string protectedCredentials = secretProtector.Protect(JsonSerializer.Serialize(credentials));
        connection.ConfigureProvider(login.RestaurantId, protectedCredentials, request.Environment);
        getirFoodClient.Invalidate(connection.Id);
        await context.SaveChangesAsync(cancellationToken);
        return Ok(Map(connection));
    }

    [HttpPut("{id:guid}/trendyol-credentials")]
    [Authorize(Policy = Permissions.IntegrationsWrite)]
    public async Task<ActionResult<IntegrationConnectionResponse>> ConfigureTrendyol(Guid id,
        ConfigureTrendyolRequest request, CancellationToken cancellationToken)
    {
        IntegrationConnection? connection = await context.Connections.FindAsync([id], cancellationToken);
        if (connection is null) return NotFound();
        if (!TenantScope.CanAccess(User, connection.BusinessId)) return Forbid();
        if (connection.Provider != IntegrationProvider.Trendyol)
            return BadRequest(new ProblemDetails { Title = "Bu bağlantı Trendyol bağlantısı değil.", Status = 400 });
        string supplierId = request.SupplierId?.Trim() ?? string.Empty;
        string storeId = request.StoreId?.Trim() ?? string.Empty;
        string email = request.ExecutorEmail?.Trim() ?? string.Empty;
        Dictionary<string, string[]> errors = [];
        if (!long.TryParse(supplierId, out _)) errors["supplierId"] = ["Satıcı ID sayısal olmalıdır."];
        if (!long.TryParse(storeId, out _)) errors["storeId"] = ["Şube (store) ID sayısal olmalıdır."];
        if (string.IsNullOrWhiteSpace(request.ApiKey) || string.IsNullOrWhiteSpace(request.ApiSecret))
            errors["credentials"] = ["API Key ve API Secret zorunludur."];
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || email.Length > 200)
            errors["executorEmail"] = ["İşlemi yapan kişinin e-posta adresi geçerli olmalıdır."];
        if (errors.Count > 0) return BadRequest(new ValidationProblemDetails(errors));
        TrendyolCredentials credentials = new(request.ApiKey.Trim(), request.ApiSecret.Trim(), storeId, email);
        // Read one page before saving: it proves the keys, supplier id and store id are accepted.
        try
        {
            await trendyolClient.GetPackagesAsync(request.Environment, supplierId, credentials, null, null, 0, 1,
                cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Trendyol check failed while configuring connection {ConnectionId}.", id);
            return BadRequest(new ProblemDetails
            {
                Title = "Trendyol API bilgileri doğrulanamadı.",
                Detail = exception switch
                {
                    HttpRequestException { StatusCode: System.Net.HttpStatusCode.ServiceUnavailable } when
                        request.Environment == ProviderEnvironment.Sandbox =>
                        "Test ortamı IP yetkilendirmesi gerektirir (503). Sunucu IP'sini Trendyol'a bildirin.",
                    HttpRequestException { StatusCode: not null } =>
                        "Trendyol bilgileri kabul etmedi. Ortamı, satıcı ID, şube ID ve API anahtarlarını kontrol edin.",
                    _ => "Trendyol servisine ulaşılamadı. Daha sonra tekrar deneyin."
                },
                Status = 400
            });
        }
        connection.ConfigureProvider(supplierId, secretProtector.Protect(JsonSerializer.Serialize(credentials)),
            request.Environment);
        await context.SaveChangesAsync(cancellationToken);
        return Ok(Map(connection));
    }

    [HttpPost("{id:guid}/test")]
    [Authorize(Policy = Permissions.IntegrationsWrite)]
    public async Task<ActionResult<IntegrationConnectionTestResponse>> TestConnection(Guid id,
        CancellationToken cancellationToken)
    {
        IntegrationConnection? connection = await context.Connections.FindAsync([id], cancellationToken);
        if (connection is null) return NotFound();
        if (!TenantScope.CanAccess(User, connection.BusinessId)) return Forbid();
        if (connection.Provider is not (IntegrationProvider.Yemeksepeti or IntegrationProvider.Getir or IntegrationProvider.Trendyol))
            return BadRequest(new ProblemDetails { Title = "Bu bağlantı için bağlantı testi desteklenmiyor.", Status = 400 });
        if (!connection.CredentialsConfigured)
            return BadRequest(new ProblemDetails { Title = connection.Provider switch
            {
                IntegrationProvider.Getir => "Önce Getir anahtarlarını kaydedin.",
                IntegrationProvider.Trendyol => "Önce Trendyol API bilgilerini kaydedin.",
                _ => "Önce Yemeksepeti OAuth bilgilerini kaydedin."
            }, Status = 400 });

        IntegrationConnectionHealthResult result = await healthChecker.CheckAsync(
            connection, automatic: false, cancellationToken);
        return Ok(new IntegrationConnectionTestResponse(result.Success, result.Environment,
            result.CheckedAtUtc, result.TokenExpiresAtUtc, result.Message));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.IntegrationsWrite)]
    public async Task<ActionResult<IntegrationConnectionResponse>> Create(CreateIntegrationConnectionRequest request, CancellationToken cancellationToken)
    {
        Guid? scopedBusinessId = TenantScope.GetBusinessId(User);
        if (scopedBusinessId.HasValue && request.BusinessId != scopedBusinessId.Value) return Forbid();
        Guid businessId = scopedBusinessId ?? request.BusinessId;
        if (businessId == Guid.Empty || request.BranchId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["connection"] = ["İşletme, şube ve bağlantı adı zorunludur."] }));
        if (!await coreOrdersClient.BranchExistsAsync(businessId, request.BranchId, cancellationToken))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["branchId"] = ["Şube aktif değil veya seçilen işletmeye ait değil."] }));

        string adapterVersion = string.IsNullOrWhiteSpace(request.AdapterVersion)
            ? request.Provider switch
            {
                IntegrationProvider.Yemeksepeti => "yemeksepeti-partner-v2",
                IntegrationProvider.Getir => "getir-food-v1",
                IntegrationProvider.Trendyol => "trendyol-webhook-v1",
                _ => "canonical-v1"
            }
            : request.AdapterVersion.Trim();
        try { adapterRegistry.Resolve(request.Provider, adapterVersion); }
        catch (ProviderPayloadException exception)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                { ["adapterVersion"] = [exception.Message] }));
        }

        string secret = SecretHasher.CreateSecret();
        IntegrationConnection connection = IntegrationConnection.Create(businessId, request.BranchId,
            request.Provider, request.Name, SecretHasher.Hash(secret), secretProtector.Protect(secret),
            request.AuthMode, adapterVersion);
        context.Connections.Add(connection);
        await context.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/integrations/{connection.Id}", Map(connection, secret));
    }

    [HttpPatch("{id:guid}/active")]
    [Authorize(Policy = Permissions.IntegrationsWrite)]
    public async Task<ActionResult<IntegrationConnectionResponse>> SetActive(Guid id, SetIntegrationActiveRequest request, CancellationToken cancellationToken)
    {
        IntegrationConnection? connection = await context.Connections.FindAsync([id], cancellationToken);
        if (connection is null) return NotFound();
        if (!TenantScope.CanAccess(User, connection.BusinessId)) return Forbid();
        connection.SetActive(request.IsActive);
        await context.SaveChangesAsync(cancellationToken);
        if (!request.IsActive && connection.Provider is IntegrationProvider.Yemeksepeti or IntegrationProvider.Getir
                or IntegrationProvider.Trendyol)
        {
            try
            {
                await coreOrdersClient.SetIntegrationHealthAsync(connection, healthy: true,
                    "Bağlantı yönetici tarafından pasife alındı.", timeProvider.GetUtcNow(), cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception,
                    "Could not resolve health alert for disabled connection {ConnectionId}.", id);
            }
        }
        return Ok(Map(connection));
    }

    [HttpPost("{id:guid}/rotate-secret")]
    [Authorize(Policy = Permissions.IntegrationsWrite)]
    public async Task<ActionResult<IntegrationConnectionResponse>> RotateSecret(Guid id, CancellationToken cancellationToken)
    {
        IntegrationConnection? connection = await context.Connections.FindAsync([id], cancellationToken);
        if (connection is null) return NotFound();
        if (!TenantScope.CanAccess(User, connection.BusinessId)) return Forbid();
        string secret = SecretHasher.CreateSecret();
        int graceMinutes = Math.Clamp(securityOptions.Value.WebhookPreviousSecretGraceMinutes, 5, 10_080);
        connection.RotateSecret(SecretHasher.Hash(secret), secretProtector.Protect(secret),
            timeProvider.GetUtcNow().AddMinutes(graceMinutes));
        await context.SaveChangesAsync(cancellationToken);
        return Ok(Map(connection, secret));
    }

    [HttpPost("events/{id:guid}/retry")]
    [Authorize(Policy = Permissions.IntegrationsWrite)]
    public async Task<ActionResult<InboundEventResponse>> RetryEvent(Guid id,
        CancellationToken cancellationToken)
    {
        InboundOrderEvent? item = await context.InboundOrderEvents.Include(x => x.Connection)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return NotFound();
        if (!TenantScope.CanAccess(User, item.Connection.BusinessId)) return Forbid();
        try { item.Retry(timeProvider.GetUtcNow()); }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails { Title = "Olay yeniden işlenemedi.",
                Detail = exception.Message, Status = 409 });
        }
        await context.SaveChangesAsync(cancellationToken);
        return Ok(Map(item));
    }

    [HttpPost("outbound-events/{id:guid}/retry")]
    [Authorize(Policy = Permissions.IntegrationsWrite)]
    public async Task<ActionResult<OutboundEventResponse>> RetryOutboundEvent(Guid id,
        CancellationToken cancellationToken)
    {
        OutboundOrderEvent? item = await context.OutboundOrderEvents.Include(x => x.Connection)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return NotFound();
        if (!TenantScope.CanAccess(User, item.Connection.BusinessId)) return Forbid();
        try { item.Retry(timeProvider.GetUtcNow()); }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails { Title = "Olay yeniden gönderilemedi.",
                Detail = exception.Message, Status = 409 });
        }
        await context.SaveChangesAsync(cancellationToken);
        return Ok(Map(item));
    }

    private static IntegrationConnectionResponse Map(IntegrationConnection item, string? secret = null) =>
        new(item.Id, item.BusinessId, item.BranchId, item.Provider, item.Name,
            item.AuthMode, item.AdapterVersion, item.IsActive, item.CreatedAtUtc,
            $"/api/v1/webhooks/{item.Id}/orders", secret, item.CredentialsConfigured,
            item.ProviderAccountId, item.ProviderEnvironment, item.LastHealthCheckAtUtc,
            item.LastHealthCheckSucceeded, item.LastHealthCheckMessage,
            item.LastTokenExpiresAtUtc, item.ConsecutiveHealthCheckFailures,
            item.LastAutomaticHealthCheckAtUtc, item.PreviousSecretValidUntilUtc);

    private static InboundEventResponse Map(InboundOrderEvent item) => new(item.Id, item.ConnectionId,
        item.ExternalEventId, item.ExternalOrderId, item.EventType, item.AdapterVersion, item.Status,
        item.Attempts, item.CoreOrderId, item.LastError, item.ReceivedAtUtc,
        item.NextAttemptAtUtc, item.ProcessedAtUtc);

    private static OutboundEventResponse Map(OutboundOrderEvent item) => new(item.Id,
        item.ConnectionId, item.CoreOrderId, item.ExternalOrderId, item.ProviderStatus,
        item.Status, item.Attempts, item.LastError, item.CreatedAtUtc,
        item.NextAttemptAtUtc, item.ProcessedAtUtc);
}

public sealed record CreateIntegrationConnectionRequest(Guid BusinessId, Guid BranchId,
    IntegrationProvider Provider, string Name, WebhookAuthMode AuthMode = WebhookAuthMode.HmacSha256,
    string? AdapterVersion = null);
public sealed record SetIntegrationActiveRequest(bool IsActive);
public sealed record IntegrationConnectionResponse(Guid Id, Guid BusinessId, Guid BranchId, IntegrationProvider Provider,
    string Name, WebhookAuthMode AuthMode, string AdapterVersion, bool IsActive,
    DateTimeOffset CreatedAtUtc, string WebhookPath, string? Secret, bool CredentialsConfigured,
    string? ProviderAccountId, ProviderEnvironment ProviderEnvironment,
    DateTimeOffset? LastHealthCheckAtUtc, bool? LastHealthCheckSucceeded,
    string? LastHealthCheckMessage, DateTimeOffset? LastTokenExpiresAtUtc,
    int ConsecutiveHealthCheckFailures, DateTimeOffset? LastAutomaticHealthCheckAtUtc,
    DateTimeOffset? PreviousSecretValidUntilUtc);
public sealed record IntegrationConnectionTestResponse(bool Success, ProviderEnvironment Environment,
    DateTimeOffset CheckedAtUtc, DateTimeOffset? TokenExpiresAtUtc, string Message);
public sealed record InboundEventResponse(Guid Id, Guid ConnectionId, string ExternalEventId,
    string ExternalOrderId, string EventType, string AdapterVersion, InboundEventStatus Status,
    int Attempts, Guid? CoreOrderId, string? LastError, DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? NextAttemptAtUtc, DateTimeOffset? ProcessedAtUtc);
public sealed record ConfigureYemeksepetiRequest(string ClientId, string ClientSecret,
    string ChainId, ProviderEnvironment Environment = ProviderEnvironment.Sandbox);
public sealed record ConfigureGetirRequest(string AppSecretKey, string RestaurantSecretKey,
    ProviderEnvironment Environment = ProviderEnvironment.Sandbox);
public sealed record ConfigureTrendyolRequest(string SupplierId, string StoreId, string ApiKey, string ApiSecret,
    string ExecutorEmail, ProviderEnvironment Environment = ProviderEnvironment.Sandbox);
public sealed record OutboundEventResponse(Guid Id, Guid ConnectionId, Guid CoreOrderId,
    string ExternalOrderId, string ProviderStatus, OutboundEventStatus Status, int Attempts,
    string? LastError, DateTimeOffset CreatedAtUtc, DateTimeOffset? NextAttemptAtUtc,
    DateTimeOffset? ProcessedAtUtc);

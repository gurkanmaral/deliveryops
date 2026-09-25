using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Security.Claims;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Security;
using DeliveryOps.Integrations.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace DeliveryOps.Integrations.UnitTests;

public sealed class IntegrationPipelineTests
{
    [Fact]
    public void Canonical_adapter_normalizes_order_and_event_metadata()
    {
        const string payload = """
            {"eventId":"evt-42","eventType":"order.created","externalOrderId":"YS-99",
             "customerName":"Ada Lovelace","customerPhone":"05550000000",
             "deliveryAddress":"Kadıköy","totalAmount":245.50,
             "deliveryLatitude":40.9901,"deliveryLongitude":29.0282,"deliveryInstructions":"Zili çalmayın"}
            """;

        AdaptedOrder result = new CanonicalV1OrderAdapter().Adapt(payload);

        Assert.Equal("evt-42", result.ExternalEventId);
        Assert.Equal("YS-99", result.Order.ExternalOrderId);
        Assert.Equal(245.50m, result.Order.TotalAmount);
        Assert.Equal(40.9901, result.Order.DeliveryLatitude);
        Assert.Equal(29.0282, result.Order.DeliveryLongitude);
        Assert.Equal("Zili çalmayın", result.Order.DeliveryInstructions);
        Assert.Equal(64, result.PayloadHash.Length);
    }

    [Fact]
    public void Retry_policy_moves_event_to_dead_letter_after_max_attempts()
    {
        DateTimeOffset now = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
        InboundOrderEvent item = InboundOrderEvent.Receive(Guid.NewGuid(), "evt", "order", "order.created",
            "canonical-v1", new string('a', 64), "{}", "{}");

        item.StartProcessing(now);
        item.Fail("temporary", now, 2);
        Assert.Equal(InboundEventStatus.Failed, item.Status);
        Assert.True(item.NextAttemptAtUtc > now);

        item.StartProcessing(now.AddMinutes(1));
        item.Fail("still failing", now.AddMinutes(1), 2);
        Assert.Equal(InboundEventStatus.DeadLettered, item.Status);
        Assert.Null(item.NextAttemptAtUtc);
    }

    [Fact]
    public void Hmac_authentication_validates_timestamp_and_raw_body()
    {
        DateTimeOffset now = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
        WebhookSecretProtector protector = new(new EphemeralDataProtectionProvider());
        const string secret = "test-signing-secret";
        IntegrationConnection connection = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            IntegrationProvider.Getir, "Getir test", SecretHasher.Hash(secret), protector.Protect(secret),
            WebhookAuthMode.HmacSha256);
        DefaultHttpContext context = new();
        string timestamp = now.ToUnixTimeSeconds().ToString();
        const string body = "{\"externalOrderId\":\"42\"}";
        string signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();
        context.Request.Headers["X-DeliveryOps-Timestamp"] = timestamp;
        context.Request.Headers["X-DeliveryOps-Signature"] = $"sha256={signature}";
        WebhookAuthenticator authenticator = new(protector, new FixedTimeProvider(now));

        Assert.True(authenticator.Verify(context.Request, connection, body));
        Assert.False(authenticator.Verify(context.Request, connection, body + " "));
    }

    [Fact]
    public void Previous_hmac_secret_is_accepted_only_during_rotation_grace_period()
    {
        DateTimeOffset now = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);
        WebhookSecretProtector protector = new(new EphemeralDataProtectionProvider());
        const string previousSecret = "previous-signing-secret";
        const string activeSecret = "active-signing-secret";
        IntegrationConnection connection = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            IntegrationProvider.Getir, "Getir rotation", SecretHasher.Hash(previousSecret),
            protector.Protect(previousSecret), WebhookAuthMode.HmacSha256);
        connection.RotateSecret(SecretHasher.Hash(activeSecret), protector.Protect(activeSecret), now.AddHours(1));
        const string body = "{\"externalOrderId\":\"rotation\"}";
        DefaultHttpContext request = SignedRequest(previousSecret, body, now);

        Assert.True(new WebhookAuthenticator(protector, new FixedTimeProvider(now))
            .Verify(request.Request, connection, body));
        Assert.False(new WebhookAuthenticator(protector, new FixedTimeProvider(now.AddHours(2)))
            .Verify(request.Request, connection, body));

        DefaultHttpContext activeRequest = SignedRequest(activeSecret, body, now.AddHours(2));
        Assert.True(new WebhookAuthenticator(protector, new FixedTimeProvider(now.AddHours(2)))
            .Verify(activeRequest.Request, connection, body));
    }

    [Fact]
    public void Previous_api_key_is_accepted_only_during_rotation_grace_period()
    {
        DateTimeOffset now = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);
        WebhookSecretProtector protector = new(new EphemeralDataProtectionProvider());
        IntegrationConnection connection = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            IntegrationProvider.Getir, "API key rotation", SecretHasher.Hash("previous-key"),
            protector.Protect("previous-key"), WebhookAuthMode.ApiKey);
        connection.RotateSecret(SecretHasher.Hash("active-key"), protector.Protect("active-key"), now.AddHours(1));
        DefaultHttpContext request = new();
        request.Request.Headers["X-DeliveryOps-Key"] = "previous-key";

        Assert.True(new WebhookAuthenticator(protector, new FixedTimeProvider(now))
            .Verify(request.Request, connection, "{}"));
        Assert.False(new WebhookAuthenticator(protector, new FixedTimeProvider(now.AddHours(2)))
            .Verify(request.Request, connection, "{}"));
    }

    [Fact]
    public void Yemeksepeti_v2_adapter_maps_official_order_payload()
    {
        const string payload = """
            {"order_id":"9d4a63b5-3e07-4440-96af-aa04797da3a0","status":"RECEIVED",
             "order_type":"DELIVERY","customer":{"first_name":"Ada","last_name":"L.",
             "phone_number":"05*******","delivery_address":{"street":"Bağdat Cad.","number":"42","city":"İstanbul",
             "latitude":40.9701,"longitude":29.0632,"instructions":"Güvenliğe bırakın"}},
             "payment":{"order_total":41.75},"sys":{"updated_at":"2026-09-18T10:05:00Z"}}
            """;

        AdaptedOrder result = new YemeksepetiPartnerV2OrderAdapter().Adapt(payload);

        Assert.Equal("9d4a63b5-3e07-4440-96af-aa04797da3a0", result.Order.ExternalOrderId);
        Assert.Equal("order.received", result.EventType);
        Assert.Equal("Ada L.", result.Order.CustomerName);
        Assert.Contains("Bağdat Cad.", result.Order.DeliveryAddress);
        Assert.Equal(41.75m, result.Order.TotalAmount);
        Assert.Equal(40.9701, result.Order.DeliveryLatitude);
        Assert.Equal(29.0632, result.Order.DeliveryLongitude);
        Assert.Equal("Güvenliğe bırakın", result.Order.DeliveryInstructions);
    }

    [Fact]
    public void Getir_food_adapter_maps_official_order_location()
    {
        const string payload = """
            {"id":"getir-42","status":100,"checkoutDate":"2026-09-24T10:00:00Z",
             "client":{"name":"Ada","clientUnmaskedPhoneNumber":"05550000000",
             "location":{"lat":40.9821,"lon":29.0712},
             "deliveryAddress":{"address":"Moda Cad. 10","description":"Kapıyı çalmayın"}},
             "clientNote":"Giriş arka sokakta","totalDiscountedPrice":310.5}
            """;

        AdaptedOrder result = new GetirFoodV1OrderAdapter().Adapt(payload);

        Assert.Equal("getir-42", result.Order.ExternalOrderId);
        Assert.Equal(40.9821, result.Order.DeliveryLatitude);
        Assert.Equal(29.0712, result.Order.DeliveryLongitude);
        Assert.Contains("Kapıyı çalmayın", result.Order.DeliveryInstructions);
        Assert.Equal(310.5m, result.Order.TotalAmount);
    }

    [Fact]
    public void Trendyol_adapter_maps_shipment_coordinates()
    {
        const string payload = """
            {"id":"9001","orderNumber":"TY-42","packageStatus":"Created","timestamp":1790244000000,
             "deliveryType":"STORE","totalPrice":455.75,"customerNote":"Zili çalmayın",
             "address":{"firstName":"Ada","lastName":"Lovelace","phone":"05550000000",
             "address1":"Caddebostan Mah. No: 8","latitude":"40.9632","longitude":"29.0638"}}
            """;

        AdaptedOrder result = new TrendyolWebhookV1OrderAdapter().Adapt(payload);

        Assert.Equal("TY-42-9001", result.Order.ExternalOrderId);
        Assert.Equal(40.9632, result.Order.DeliveryLatitude);
        Assert.Equal(29.0638, result.Order.DeliveryLongitude);
        Assert.Equal(455.75m, result.Order.TotalAmount);
        Assert.Equal(ProviderDeliveryFulfillment.MerchantCourier, result.Order.DeliveryFulfillment);
        Assert.Equal("Zili çalmayın", result.Order.DeliveryInstructions);
    }

    [Fact]
    public void Trendyol_go_order_is_not_assigned_to_merchant_courier()
    {
        const string payload = """
            {"id":"9002","orderNumber":"TY-43","packageStatus":"Created","timestamp":1790244000000,
             "deliveryType":"GO","totalPrice":200,"address":{"firstName":"Ada","lastName":"L.",
             "phone":"08502419090","address1":"Uber Eats Trendyol Go - Yemek"}}
            """;

        AdaptedOrder result = new TrendyolWebhookV1OrderAdapter().Adapt(payload);

        Assert.Equal(ProviderDeliveryFulfillment.ProviderCourier, result.Order.DeliveryFulfillment);
        Assert.Null(result.Order.DeliveryLatitude);
    }

    [Fact]
    public void Trendyol_lifecycle_event_uses_same_external_order_id_without_customer_payload()
    {
        const string payload = """
            {"id":"9001","orderNumber":"TY-42","packageStatus":"Cancelled","timestamp":1790245000000,
             "cancelInfo":{"reason":"Müşteri iptal etti"}}
            """;

        AdaptedOrder result = new TrendyolWebhookV1OrderAdapter().Adapt(payload);

        Assert.Equal("TY-42-9001", result.Order.ExternalOrderId);
        Assert.Equal("order.cancelled", result.EventType);
    }

    [Fact]
    public void Provider_adapter_rejects_partial_coordinates()
    {
        const string payload = """
            {"externalOrderId":"bad-location","customerName":"Ada","customerPhone":"05550000000",
             "deliveryAddress":"Kadıköy","totalAmount":10,"deliveryLatitude":40.99}
            """;

        Assert.Throws<ProviderPayloadException>(() => new CanonicalV1OrderAdapter().Adapt(payload));
    }

    [Fact]
    public void Static_authorization_mode_compares_the_full_header_secret()
    {
        WebhookSecretProtector protector = new(new EphemeralDataProtectionProvider());
        const string secret = "Basic cGFydG5lcjpwYXNzd29yZA==";
        IntegrationConnection connection = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            IntegrationProvider.Yemeksepeti, "YS", SecretHasher.Hash(secret), protector.Protect(secret),
            WebhookAuthMode.StaticAuthorization, "yemeksepeti-partner-v2");
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = secret;

        Assert.True(new WebhookAuthenticator(protector, TimeProvider.System)
            .Verify(context.Request, connection, "{}"));
    }

    [Fact]
    public async Task Yemeksepeti_client_reuses_oauth_token_and_sends_official_status_contract()
    {
        DateTimeOffset now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        WebhookSecretProtector protector = new(new EphemeralDataProtectionProvider());
        IntegrationConnection connection = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            IntegrationProvider.Yemeksepeti, "YS sandbox", SecretHasher.Hash("webhook"),
            protector.Protect("webhook"), WebhookAuthMode.StaticAuthorization,
            "yemeksepeti-partner-v2");
        connection.ConfigureProvider("chain-42", protector.Protect(JsonSerializer.Serialize(
            new YemeksepetiCredentials("client-id", "client-secret"))), ProviderEnvironment.Sandbox);
        RecordingHandler handler = new();
        YemeksepetiPartnerClient client = new(new FakeHttpClientFactory(handler), protector,
            new FixedTimeProvider(now));
        const string inbound = """
            {"items":[{"_id":"item-1","sku":"101049","status":"IN_CART",
            "pricing":{"pricing_type":"UNIT","quantity":1,"unit_price":39.75}}]}
            """;

        await client.UpdateOrderStatusAsync(connection, "order-1", "READY_FOR_PICKUP", null, inbound, default);
        await client.UpdateOrderStatusAsync(connection, "order-2", "CANCELLED", "OTHER", inbound, default);

        Assert.Single(handler.Requests, x => x.Path.EndsWith("/v2/oauth/token"));
        RecordedRequest[] updates = handler.Requests.Where(x => x.Method == HttpMethod.Put).ToArray();
        Assert.Equal(2, updates.Length);
        Assert.All(updates, x => Assert.Equal("Bearer token-42", x.Authorization));
        Assert.Contains("\"status\":\"READY_FOR_PICKUP\"", updates[0].Body);
        Assert.Contains("\"items\":[", updates[0].Body);
        Assert.Contains("\"cancellation\":", updates[1].Body);
    }

    [Fact]
    public async Task Yemeksepeti_connection_test_forces_a_fresh_token_and_returns_expiry()
    {
        DateTimeOffset now = new(2026, 9, 19, 9, 30, 0, TimeSpan.Zero);
        WebhookSecretProtector protector = new(new EphemeralDataProtectionProvider());
        IntegrationConnection connection = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            IntegrationProvider.Yemeksepeti, "YS sandbox", SecretHasher.Hash("webhook"),
            protector.Protect("webhook"), WebhookAuthMode.StaticAuthorization,
            "yemeksepeti-partner-v2");
        connection.ConfigureProvider("chain-42", protector.Protect(JsonSerializer.Serialize(
            new YemeksepetiCredentials("client-id", "client-secret"))), ProviderEnvironment.Sandbox);
        RecordingHandler handler = new();
        YemeksepetiPartnerClient client = new(new FakeHttpClientFactory(handler), protector,
            new FixedTimeProvider(now));

        YemeksepetiConnectionTestResult first = await client.TestConnectionAsync(connection, default);
        YemeksepetiConnectionTestResult second = await client.TestConnectionAsync(connection, default);

        Assert.Equal(2, handler.Requests.Count(x => x.Path.EndsWith("/v2/oauth/token")));
        Assert.Equal(ProviderEnvironment.Sandbox, first.Environment);
        Assert.Equal(now.AddHours(2), first.TokenExpiresAtUtc);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Provider_reconfiguration_clears_previous_health_result()
    {
        DateTimeOffset now = new(2026, 9, 19, 9, 30, 0, TimeSpan.Zero);
        WebhookSecretProtector protector = new(new EphemeralDataProtectionProvider());
        IntegrationConnection connection = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            IntegrationProvider.Yemeksepeti, "YS sandbox", SecretHasher.Hash("webhook"),
            protector.Protect("webhook"), WebhookAuthMode.StaticAuthorization,
            "yemeksepeti-partner-v2");
        string protectedCredentials = protector.Protect(JsonSerializer.Serialize(
            new YemeksepetiCredentials("client-id", "client-secret")));
        connection.ConfigureProvider("chain-42", protectedCredentials, ProviderEnvironment.Sandbox);
        connection.RecordHealthCheck(true, "OAuth kimlik bilgileri doğrulandı.", now, now.AddHours(2));

        connection.ConfigureProvider("chain-43", protectedCredentials, ProviderEnvironment.Production);

        Assert.Null(connection.LastHealthCheckAtUtc);
        Assert.Null(connection.LastHealthCheckSucceeded);
        Assert.Null(connection.LastHealthCheckMessage);
        Assert.Null(connection.LastTokenExpiresAtUtc);
        Assert.Equal(0, connection.ConsecutiveHealthCheckFailures);
        Assert.Null(connection.LastAutomaticHealthCheckAtUtc);
    }

    [Fact]
    public void Health_results_count_consecutive_failures_and_success_resets_them()
    {
        DateTimeOffset now = new(2026, 9, 19, 10, 0, 0, TimeSpan.Zero);
        WebhookSecretProtector protector = new(new EphemeralDataProtectionProvider());
        IntegrationConnection connection = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            IntegrationProvider.Yemeksepeti, "YS", SecretHasher.Hash("webhook"),
            protector.Protect("webhook"), WebhookAuthMode.StaticAuthorization,
            "yemeksepeti-partner-v2");

        connection.RecordHealthCheck(false, "Bağlantı kurulamadı.", now, null, automatic: true);
        connection.RecordHealthCheck(false, "Bağlantı kurulamadı.", now.AddMinutes(15), null,
            automatic: true);

        Assert.Equal(2, connection.ConsecutiveHealthCheckFailures);
        Assert.Equal(now.AddMinutes(15), connection.LastAutomaticHealthCheckAtUtc);

        connection.RecordHealthCheck(true, "OAuth kimlik bilgileri doğrulandı.",
            now.AddMinutes(30), now.AddHours(2), automatic: true);
        Assert.Equal(0, connection.ConsecutiveHealthCheckFailures);
        Assert.True(connection.LastHealthCheckSucceeded);
    }

    [Fact]
    public void Health_history_never_keeps_token_expiry_for_failed_checks()
    {
        DateTimeOffset now = new(2026, 9, 19, 11, 0, 0, TimeSpan.Zero);

        IntegrationHealthCheck check = IntegrationHealthCheck.Create(Guid.NewGuid(), false,
            " Client doğrulanamadı. ", ProviderEnvironment.Sandbox, true, now, now.AddHours(2));

        Assert.False(check.Succeeded);
        Assert.Equal("Client doğrulanamadı.", check.Message);
        Assert.True(check.Automatic);
        Assert.Null(check.TokenExpiresAtUtc);
    }

    [Fact]
    public void Tenant_scope_never_allows_a_business_user_to_cross_business_boundaries()
    {
        Guid ownBusinessId = Guid.NewGuid();
        ClaimsPrincipal businessUser = new(new ClaimsIdentity(
            [new Claim("business_id", ownBusinessId.ToString())], "test"));

        Assert.True(TenantScope.CanAccess(businessUser, ownBusinessId));
        Assert.False(TenantScope.CanAccess(businessUser, Guid.NewGuid()));
        Assert.Equal(ownBusinessId, TenantScope.ResolveBusinessId(businessUser, Guid.NewGuid()));

        ClaimsPrincipal platformUser = new(new ClaimsIdentity([], "test"));
        Assert.True(TenantScope.CanAccess(platformUser, Guid.NewGuid()));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string Body, string? Authorization);

    private static DefaultHttpContext SignedRequest(string secret, string body, DateTimeOffset timestamp)
    {
        DefaultHttpContext context = new();
        string unixSeconds = timestamp.ToUnixTimeSeconds().ToString();
        string signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{unixSeconds}.{body}"))).ToLowerInvariant();
        context.Request.Headers["X-DeliveryOps-Timestamp"] = unixSeconds;
        context.Request.Headers["X-DeliveryOps-Signature"] = $"sha256={signature}";
        return context;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty :
                await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, body,
                request.Headers.Authorization?.ToString()));
            return request.RequestUri.AbsolutePath.EndsWith("/v2/oauth/token")
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"token-42\",\"expires_in\":7200}")
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Security;
using DeliveryOps.Integrations.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeliveryOps.Integrations.UnitTests;

public sealed class GetirIntegrationTests
{
    private const string NewOrderPayload = """
        {"id":"65f0c0ffee","status":400,"isScheduled":false,"confirmationId":"ABC1",
         "client":{"name":"Ada","clientUnmaskedPhoneNumber":"05550000000",
                   "deliveryAddress":{"address":"Kadıköy Moda Cd. 1","description":"Kat 2"},
                   "location":{"lat":40.99,"lon":29.03}},
         "courier":{"id":"c1","status":100},
         "clientNote":"Acısız olsun","doNotKnock":true,"dropOffAtDoor":false,
         "totalPrice":250,"totalDiscountedPrice":220,"checkoutDate":"2026-09-30T12:00:00Z",
         "deliveryType":2,"paymentMethod":1,"paymentMethodText":{"tr":"Kapıda Nakit","en":"Cash"}}
        """;

    [Fact]
    public void Adapter_uses_delivery_type_for_courier_and_adds_courier_notes()
    {
        AdaptedOrder result = new GetirFoodV1OrderAdapter().Adapt(NewOrderPayload);

        Assert.Equal("order.created", result.EventType);
        Assert.Equal("65f0c0ffee", result.Order.ExternalOrderId);
        Assert.Equal(220m, result.Order.TotalAmount);
        // deliveryType 2 = restaurant's own courier even though a courier object is present.
        Assert.Equal(ProviderDeliveryFulfillment.MerchantCourier, result.Order.DeliveryFulfillment);
        Assert.Contains("Zili çalmayın", result.Order.DeliveryInstructions);
        Assert.Contains("Ödeme: Kapıda Nakit", result.Order.DeliveryInstructions);
        Assert.DoesNotContain("Kapıya bırakın", result.Order.DeliveryInstructions);
    }

    [Fact]
    public void Adapter_maps_getir_courier_and_cancellation_payloads()
    {
        string getirCourier = NewOrderPayload.Replace("\"deliveryType\":2", "\"deliveryType\":1");
        string cancelled = NewOrderPayload.Replace("\"deliveryType\":2",
            "\"deliveryType\":2,\"cancelDate\":\"2026-09-30T12:05:00Z\",\"cancelNote\":\"Müşteri vazgeçti\"," +
            "\"cancelReason\":{\"id\":\"r1\",\"messages\":{\"tr\":\"Müşteri iptal etti\"}}");

        AdaptedOrder courier = new GetirFoodV1OrderAdapter().Adapt(getirCourier);
        AdaptedOrder cancel = new GetirFoodV1OrderAdapter().Adapt(cancelled);

        Assert.Equal(ProviderDeliveryFulfillment.ProviderCourier, courier.Order.DeliveryFulfillment);
        Assert.Equal("order.cancelled", cancel.EventType);
        Assert.Equal("65f0c0ffee:cancelled", cancel.ExternalEventId);
    }

    [Fact]
    public void Api_key_mode_accepts_getir_x_api_key_header()
    {
        WebhookSecretProtector protector = new(new EphemeralDataProtectionProvider());
        IntegrationConnection connection = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            IntegrationProvider.Getir, "Getir", SecretHasher.Hash("shared-key"), protector.Protect("shared-key"),
            WebhookAuthMode.ApiKey, "getir-food-v1");
        DefaultHttpContext valid = new();
        valid.Request.Headers["x-api-key"] = "shared-key";
        DefaultHttpContext invalid = new();
        invalid.Request.Headers["x-api-key"] = "wrong";

        WebhookAuthenticator authenticator = new(protector, TimeProvider.System);
        Assert.True(authenticator.Verify(valid.Request, connection, "{}"));
        Assert.False(authenticator.Verify(invalid.Request, connection, "{}"));
    }

    [Fact]
    public async Task Verify_logs_in_sends_token_and_treats_already_verified_as_success()
    {
        FakeGetir getir = new();
        getir.On("POST", "/food-orders/o1/verify", HttpStatusCode.BadRequest,
            """{"code":2,"error":"FoodOrderAlreadyVerified","message":"This food order already verified"}""");
        (GetirFoodClient client, IntegrationConnection connection) = Create(getir);

        await client.VerifyAsync(connection, "o1", scheduled: false, CancellationToken.None);

        Assert.Equal(["POST /auth/login", "POST /food-orders/o1/verify"], getir.Calls);
        Assert.Equal("token-1", getir.LastToken);
    }

    [Fact]
    public async Task Failed_transition_is_success_when_getir_already_shows_the_step_done()
    {
        FakeGetir getir = new();
        getir.On("POST", "/food-orders/o1/prepare", HttpStatusCode.BadRequest,
            """{"code":3,"error":"FoodOrderStatusInvalidError"}""");
        getir.On("GET", "/food-orders/o1", HttpStatusCode.OK,
            """{"id":"o1","status":550,"prepareDate":"2026-09-30T12:02:00Z","deliveryType":1}""");
        (GetirFoodClient client, IntegrationConnection connection) = Create(getir);

        await client.PrepareAsync(connection, "o1", CancellationToken.None);

        Assert.Contains("GET /food-orders/o1", getir.Calls);
    }

    [Fact]
    public async Task Too_early_transition_throws_so_the_event_is_retried()
    {
        FakeGetir getir = new();
        getir.On("POST", "/food-orders/o1/deliver", HttpStatusCode.BadRequest,
            """{"code":62,"error":"FoodOrderDeliveredToClientTimeLimitError"}""");
        getir.On("GET", "/food-orders/o1", HttpStatusCode.OK, """{"id":"o1","status":550,"deliveryType":2}""");
        (GetirFoodClient client, IntegrationConnection connection) = Create(getir);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.DeliverAsync(connection, "o1", CancellationToken.None));
    }

    [Fact]
    public async Task Expired_token_is_renewed_once()
    {
        FakeGetir getir = new();
        getir.OnceUnauthorized("POST", "/food-orders/o1/handover");
        (GetirFoodClient client, IntegrationConnection connection) = Create(getir);

        await client.HandoverAsync(connection, "o1", CancellationToken.None);

        Assert.Equal(2, getir.Calls.Count(x => x == "POST /auth/login"));
        Assert.Equal("token-2", getir.LastToken);
    }

    [Fact]
    public async Task Cancelled_order_raises_a_dedicated_exception()
    {
        FakeGetir getir = new();
        getir.On("POST", "/food-orders/o1/verify", HttpStatusCode.BadRequest,
            """{"code":13,"error":"FoodOrderAlreadyCancelled"}""");
        (GetirFoodClient client, IntegrationConnection connection) = Create(getir);

        await Assert.ThrowsAsync<GetirOrderCancelledException>(() =>
            client.VerifyAsync(connection, "o1", false, CancellationToken.None));
    }

    private static (GetirFoodClient, IntegrationConnection) Create(FakeGetir getir)
    {
        WebhookSecretProtector protector = new(new EphemeralDataProtectionProvider());
        IntegrationConnection connection = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            IntegrationProvider.Getir, "Getir", SecretHasher.Hash("k"), protector.Protect("k"),
            WebhookAuthMode.ApiKey, "getir-food-v1");
        connection.ConfigureProvider("restaurant-1",
            protector.Protect(JsonSerializer.Serialize(new GetirCredentials("app", "restaurant"))),
            ProviderEnvironment.Sandbox);
        GetirFoodClient client = new(new SingleClientFactory(new HttpClient(getir)), protector,
            TimeProvider.System, NullLogger<GetirFoodClient>.Instance);
        return (client, connection);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    /// <summary>Plays the GetirFood gateway: login issues token-1, token-2, … and routes are scripted.</summary>
    private sealed class FakeGetir : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes = [];
        private readonly HashSet<string> _unauthorizedOnce = [];
        private int _logins;
        public List<string> Calls { get; } = [];
        public string? LastToken { get; private set; }

        public void On(string method, string path, HttpStatusCode status, string body) =>
            _routes[$"{method} {path}"] = (status, body);

        public void OnceUnauthorized(string method, string path) => _unauthorizedOnce.Add($"{method} {path}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
            Calls.Add(key);
            Assert.StartsWith("https://food-external-api-gateway.development.getirapi.com/", request.RequestUri.ToString());
            if (key == "POST /auth/login")
            {
                string body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Contains("\"appSecretKey\":\"app\"", body);
                Assert.Contains("\"restaurantSecretKey\":\"restaurant\"", body);
                _logins++;
                return Json(HttpStatusCode.OK, $$"""{"restaurantId":"restaurant-1","token":"token-{{_logins}}"}""");
            }
            LastToken = request.Headers.TryGetValues("token", out var values) ? values.Single() : null;
            if (_unauthorizedOnce.Remove(key)) return Json(HttpStatusCode.Unauthorized, """{"code":-1}""");
            return _routes.TryGetValue(key, out var route) ? Json(route.Status, route.Body) : Json(HttpStatusCode.OK, "{}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

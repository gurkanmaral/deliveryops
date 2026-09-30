using System.Net;
using System.Text;
using System.Text.Json;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Security;
using DeliveryOps.Integrations.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeliveryOps.Integrations.UnitTests;

public sealed class TrendyolIntegrationTests
{
    // Shape of GET /packages "Model 1" (own courier) from developers.tgoapps.com, trimmed.
    private const string OwnCourierPackage = """
        {"id":"4dc2e9573983ce9fb97aa905df33f06fc3717b775a1d8391de1ca3750a9de6a4","supplierId":107385,"storeId":153,
         "orderCode":"1E1","storePickupSelected":false,"deliveryType":"STORE","preparationTime":0,
         "orderId":"1001199521762","orderNumber":"1199521762","totalPrice":602.7,"callCenterPhone":"0212 365 34 03",
         "customer":{"id":35018440,"firstName":"Oms","lastName":"M"},
         "payment":{"paymentType":"PAY_WITH_CARD","mealCard":null,"onDelivery":null},
         "address":{"firstName":"Oms","lastName":"Meal","address1":"Gündoğdu Koleji Yanı, 9 kat 2 daire","city":"İstanbul",
           "district":"Kadıköy","neighborhood":"Caferağa","apartmentNumber":"TGO Yemek","floor":"2TGO Yemek",
           "doorNumber":"TGO Yemek","addressDescription":"Kadıköy - ISA2","latitude":"40.983687","longitude":"29.0272959",
           "phone":"0212 365 34 03","pinCode":"673985557"},
         "packageStatus":"Created",
         "lines":[{"price":145,"items":[{"packageItemId":"1000008723596","isCancelled":false},
                                        {"packageItemId":"1000008723597","isCancelled":true}]},
                  {"price":75,"items":[{"packageItemId":"1000008723598","isCancelled":false}]}],
         "customerNote":"Servis İstiyorum","lastModifiedDate":1783520347000}
        """;

    [Fact]
    public void Adapter_maps_own_courier_meal_package()
    {
        AdaptedOrder result = new TrendyolWebhookV1OrderAdapter().Adapt(OwnCourierPackage);

        Assert.Equal("1199521762-4dc2e9573983ce9fb97aa905df33f06fc3717b775a1d8391de1ca3750a9de6a4",
            result.Order.ExternalOrderId);
        Assert.Equal($"{result.Order.ExternalOrderId}:CREATED", result.ExternalEventId);
        Assert.Equal(ProviderDeliveryFulfillment.MerchantCourier, result.Order.DeliveryFulfillment);
        Assert.Equal(40.983687, result.Order.DeliveryLatitude);
        // address1 is kept as free text; masked apartment placeholders are dropped.
        Assert.Equal("Gündoğdu Koleji Yanı, 9 kat 2 daire, Caferağa, Kadıköy, İstanbul", result.Order.DeliveryAddress);
        Assert.Contains("Müşteri arama: 0212 365 34 03 · kod 673985557", result.Order.DeliveryInstructions);
        Assert.Contains("Sipariş kodu: 1E1", result.Order.DeliveryInstructions);
        Assert.Contains("Servis İstiyorum", result.Order.DeliveryInstructions);
        Assert.Equal(InboundPaymentMethod.Online, result.Order.Payment!.Method);
        Assert.True(result.Order.Payment.IsPaid);
    }

    [Fact]
    public void Adapter_accepts_trendyol_courier_placeholders_and_cash_on_delivery()
    {
        string package = OwnCourierPackage
            .Replace("\"deliveryType\":\"STORE\"", "\"deliveryType\":\"GO\"")
            .Replace("\"latitude\":\"40.983687\",\"longitude\":\"29.0272959\"", "\"latitude\":\"TGO Yemek\",\"longitude\":\"TGO Yemek\"")
            .Replace("\"paymentType\":\"PAY_WITH_CARD\",\"mealCard\":null,\"onDelivery\":null",
                "\"paymentType\":\"PAY_WITH_ON_DELIVERY\",\"mealCard\":null,\"onDelivery\":{\"paymentType\":\"CASH\"}");

        AdaptedOrder result = new TrendyolWebhookV1OrderAdapter().Adapt(package);

        Assert.Equal(ProviderDeliveryFulfillment.ProviderCourier, result.Order.DeliveryFulfillment);
        Assert.Null(result.Order.DeliveryLatitude);
        Assert.Equal(InboundPaymentMethod.Cash, result.Order.Payment!.Method);
        Assert.False(result.Order.Payment.IsPaid);
        Assert.Contains("Ödeme: Kapıda nakit", result.Order.DeliveryInstructions);
    }

    [Fact]
    public void Same_status_read_again_maps_to_the_same_event()
    {
        string later = OwnCourierPackage.Replace("1783520347000", "1783520999000");

        Assert.Equal(new TrendyolWebhookV1OrderAdapter().Adapt(OwnCourierPackage).ExternalEventId,
            new TrendyolWebhookV1OrderAdapter().Adapt(later).ExternalEventId);
        // Accepted on Trendyol's tablet before we saw it: still imported as a new order.
        AdaptedOrder picking = new TrendyolWebhookV1OrderAdapter().Adapt(
            OwnCourierPackage.Replace("\"packageStatus\":\"Created\"", "\"packageStatus\":\"Picking\""));
        Assert.Equal("order.created", picking.EventType);
    }

    [Fact]
    public void Cancellation_uses_every_item_that_is_not_cancelled_yet()
    {
        Assert.Equal(["1000008723596", "1000008723598"], OutboundEventProcessor.ReadTrendyolItemIds(OwnCourierPackage));
        (string? packageId, int preparation) = InboundEventProcessor.ReadTrendyolPackage(OwnCourierPackage);
        Assert.StartsWith("4dc2e957", packageId);
        Assert.Equal(0, preparation);
    }

    [Fact]
    public async Task Package_read_sends_documented_headers_and_filters()
    {
        FakeTrendyol trendyol = new();
        trendyol.On("GET", "/integrator/order/meal/suppliers/107385/packages", HttpStatusCode.OK,
            $$"""{"page":0,"size":50,"totalPages":1,"totalCount":1,"content":[{{OwnCourierPackage}}]}""");
        (TrendyolGoClient client, IntegrationConnection connection) = Create(trendyol);

        TrendyolPackagePage page = await client.GetPackagesAsync(connection, 1000, 2000, 0, CancellationToken.None);

        Assert.Single(page.Packages);
        HttpRequestMessage request = trendyol.Requests.Single();
        Assert.StartsWith("https://stageapi.tgoapis.com/integrator/", request.RequestUri!.ToString());
        Assert.Contains("storeId=153", request.RequestUri.Query);
        Assert.Contains("packageModificationStartDate=1000", request.RequestUri.Query);
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("key:secret")), request.Headers.Authorization.Parameter);
        Assert.Equal("107385 - SelfIntegration", string.Join(" ", request.Headers.GetValues("User-Agent")));
        Assert.Equal("SelfIntegration", request.Headers.GetValues("x-agentname").Single());
        Assert.Equal("kasa@restoran.com", request.Headers.GetValues("x-executor-user").Single());
    }

    [Fact]
    public async Task Accept_sends_package_and_preparation_time()
    {
        FakeTrendyol trendyol = new();
        (TrendyolGoClient client, IntegrationConnection connection) = Create(trendyol);

        await client.AcceptAsync(connection, "p1", 25, CancellationToken.None);

        Assert.Equal("PUT /integrator/order/meal/suppliers/107385/packages/picked", trendyol.Calls.Single());
        Assert.Equal("""{"packageId":"p1","preparationTime":25}""", trendyol.Bodies.Single());
    }

    [Fact]
    public async Task Failed_step_is_success_when_package_already_moved_past_it()
    {
        FakeTrendyol trendyol = new();
        trendyol.On("PUT", "/integrator/order/meal/suppliers/107385/packages/invoiced", HttpStatusCode.BadRequest, "{}");
        trendyol.On("GET", "/integrator/order/meal/suppliers/107385/packages/p1", HttpStatusCode.OK,
            """{"id":"p1","packageStatus":"Shipped"}""");
        (TrendyolGoClient client, IntegrationConnection connection) = Create(trendyol);

        await client.InvoiceAsync(connection, "p1", CancellationToken.None);
    }

    [Fact]
    public async Task Step_on_cancelled_package_raises_dedicated_exception_and_early_step_retries()
    {
        FakeTrendyol trendyol = new();
        trendyol.On("PUT", "/integrator/order/meal/suppliers/107385/packages/p1/manual-delivered", HttpStatusCode.BadRequest, "{}");
        trendyol.On("GET", "/integrator/order/meal/suppliers/107385/packages/p1", HttpStatusCode.OK,
            """{"content":[{"id":"p1","packageStatus":"Cancelled"}]}""");
        (TrendyolGoClient client, IntegrationConnection connection) = Create(trendyol);

        await Assert.ThrowsAsync<TrendyolPackageCancelledException>(() =>
            client.DeliverAsync(connection, "p1", CancellationToken.None));

        trendyol.On("GET", "/integrator/order/meal/suppliers/107385/packages/p1", HttpStatusCode.OK,
            """{"id":"p1","packageStatus":"Invoiced"}""");
        await Assert.ThrowsAsync<HttpRequestException>(() => client.DeliverAsync(connection, "p1", CancellationToken.None));
    }

    private static (TrendyolGoClient, IntegrationConnection) Create(FakeTrendyol trendyol)
    {
        WebhookSecretProtector protector = new(new EphemeralDataProtectionProvider());
        IntegrationConnection connection = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(),
            IntegrationProvider.Trendyol, "Trendyol", SecretHasher.Hash("k"), protector.Protect("k"),
            WebhookAuthMode.ApiKey, "trendyol-webhook-v1");
        connection.ConfigureProvider("107385",
            protector.Protect(JsonSerializer.Serialize(new TrendyolCredentials("key", "secret", "153", "kasa@restoran.com"))),
            ProviderEnvironment.Sandbox);
        TrendyolGoClient client = new(new SingleClientFactory(new HttpClient(trendyol)), protector,
            new ConfigurationBuilder().Build(), NullLogger<TrendyolGoClient>.Instance);
        return (client, connection);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeTrendyol : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes = [];
        public List<string> Calls { get; } = [];
        public List<string> Bodies { get; } = [];
        public List<HttpRequestMessage> Requests { get; } = [];

        public void On(string method, string path, HttpStatusCode status, string body) =>
            _routes[$"{method} {path}"] = (status, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
            Calls.Add(key);
            Requests.Add(request);
            if (request.Content is not null) Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            (HttpStatusCode status, string body) = _routes.TryGetValue(key, out var route) ? route : (HttpStatusCode.OK, "{}");
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}

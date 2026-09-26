using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ClosedXML.Excel;
using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Api.Realtime;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Billing;
using DeliveryOps.Core.Queries.Locations;
using DeliveryOps.Core.Queries.Orders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DeliveryOps.Core.IntegrationTests;

public sealed class AuthorizationTests : IClassFixture<CoreApiFactory>
{
    private readonly HttpClient _client;
    private readonly CoreApiFactory _factory;
    public AuthorizationTests(CoreApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_unauthorized()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/v1/businesses");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Platform_admin_token_cannot_call_internal_service_endpoint()
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/v1/internal/orders",
            Permissions.OrdersWrite, platformAdmin: true);
        request.Content = JsonContent.Create(new
        {
            businessId = Guid.NewGuid(), branchId = Guid.NewGuid(), externalId = "forbidden",
            customerName = "Test", customerPhone = "555", deliveryAddress = "Test",
            source = OrderSource.Pos, totalAmount = 1
        });

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Read_permission_cannot_write_businesses()
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/v1/businesses", Permissions.BusinessesRead);
        request.Content = JsonContent.Create(new { name = "Forbidden Business", taxNumber = "1234567890" });
        HttpResponseMessage response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Read_permission_can_list_businesses()
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Get, "/api/v1/businesses", Permissions.BusinessesRead, platformAdmin: true);
        HttpResponseMessage response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Orders_endpoint_filters_sorts_and_pages_on_the_server()
    {
        Guid businessId;
        Guid expectedOrderId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Business business = Business.Create($"Order filter {Guid.NewGuid():N}", $"F{Guid.NewGuid():N}"[..12]);
            Branch branch = Branch.Create(business.Id, "Merkez", "Test adresi", null, null);
            Order phoneOrder = Order.Create(business.Id, branch.Id, "", "Telefon Müşteri", "05550000001",
                "Adres 1", OrderSource.Phone, 80m, Guid.NewGuid().ToString("N"), new string('A', 64), Guid.NewGuid());
            Order getirOrder = Order.Create(business.Id, branch.Id, "GETIR-1", "Getir Müşteri", "05550000002",
                "Adres 2", OrderSource.Getir, 240m, Guid.NewGuid().ToString("N"), new string('B', 64), Guid.NewGuid());
            context.AddRange(business, branch, phoneOrder, getirOrder);
            await context.SaveChangesAsync();
            businessId = business.Id;
            expectedOrderId = getirOrder.Id;
        }

        using HttpRequestMessage request = Authorized(HttpMethod.Get,
            $"/api/v1/orders?businessId={businessId}&source={(int)OrderSource.Getir}&minAmount=100&sort=-amount&page=1&pageSize=1",
            Permissions.OrdersRead, platformAdmin: true);
        HttpResponseMessage response = await _client.SendAsync(request);
        PagedResponse<OrderResponse>? page = await response.Content.ReadFromJsonAsync<PagedResponse<OrderResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(expectedOrderId, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task Orders_endpoint_rejects_invalid_filter_ranges()
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Get,
            "/api/v1/orders?createdFrom=2026-09-23&createdTo=2026-09-01&minAmount=200&maxAmount=100",
            Permissions.OrdersRead, platformAdmin: true);

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_business_also_creates_zero_balance_credit_account()
    {
        string code = $"C{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/v1/businesses",
            Permissions.BusinessesWrite, platformAdmin: true);
        request.Headers.Add("X-Test-Permission", Permissions.BusinessesRead);
        request.Content = JsonContent.Create(new { name = "Credit Ready Business", code });

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using IServiceScope scope = _factory.Services.CreateScope();
        CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Guid businessId = await context.Businesses.Where(x => x.Code == code).Select(x => x.Id).SingleAsync();
        BusinessCreditAccount account = await context.BusinessCreditAccounts.SingleAsync(x => x.BusinessId == businessId);
        Assert.Equal(0, account.Balance);
    }

    [Fact]
    public async Task Credit_package_top_up_is_idempotent()
    {
        (Guid businessId, _) = await AddBusinesses();
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            context.BusinessCreditAccounts.Add(BusinessCreditAccount.Create(businessId));
            await context.SaveChangesAsync();
        }
        string key = Guid.NewGuid().ToString();

        async Task<HttpResponseMessage> TopUp()
        {
            using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/v1/credits/top-up",
                Permissions.CreditsWrite, platformAdmin: true);
            request.Headers.Add("Idempotency-Key", key);
            request.Content = JsonContent.Create(new { businessId, packageCode = "CREDIT_1000", description = "Test paketi" });
            return await _client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.OK, (await TopUp()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TopUp()).StatusCode);
        using IServiceScope checkScope = _factory.Services.CreateScope();
        CoreDbContext checkContext = checkScope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(1_000, (await checkContext.BusinessCreditAccounts.SingleAsync(x => x.BusinessId == businessId)).Balance);
        Assert.Equal(1, await checkContext.CreditTransactions.CountAsync(x => x.BusinessId == businessId));
        Assert.True(await checkContext.AuditLogs.AnyAsync(x => x.BusinessId == businessId && x.EntityName == nameof(CreditTransaction)));
    }

    [Fact]
    public async Task Orders_claim_permission_can_open_courier_queue()
    {
        Guid businessId;
        Guid branchId;
        Guid courierId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Business business = Business.Create($"Courier identity {Guid.NewGuid():N}", $"CI{Guid.NewGuid():N}"[..12]);
            Branch branch = Branch.Create(business.Id, "Merkez", "Test adresi", 40.9909, 29.0283);
            Courier courier = Courier.Create(business.Id, branch.Id, "Test", "Kurye", $"5{Random.Shared.NextInt64(100000000, 999999999)}");
            courier.SetAvailability(CourierAvailability.Available);
            context.AddRange(business, branch, courier,
                CourierShift.Start(courier.Id, business.Id, DateTimeOffset.UtcNow.AddHours(-1)),
                CourierLocation.Create(courier.Id, business.Id, 40.9911, 29.0290, 5, null, null,
                    DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
            businessId = business.Id;
            branchId = branch.Id;
            courierId = courier.Id;
        }
        using HttpRequestMessage request = Authorized(HttpMethod.Get, "/api/v1/orders/available", Permissions.OrdersClaim);
        request.Headers.Add("X-Test-Permission", Permissions.OrdersRead);
        request.Headers.Add("X-Test-Business", businessId.ToString());
        request.Headers.Add("X-Test-Branch", branchId.ToString());
        request.Headers.Add("X-Test-Courier", courierId.ToString());
        HttpResponseMessage response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Courier_queue_returns_only_nearby_pickups_and_does_not_expose_customer_pii()
    {
        Guid businessId;
        Guid courierId;
        Guid nearbyOrderId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Business business = Business.Create($"Nearby queue {Guid.NewGuid():N}", $"N{Guid.NewGuid():N}"[..12]);
            Branch nearbyBranch = Branch.Create(business.Id, "Yakın Şube", "Kadıköy merkez", 40.9909, 29.0283);
            Branch farBranch = Branch.Create(business.Id, "Uzak Şube", "Sarıyer merkez", 41.1664, 29.0500);
            Courier courier = Courier.Create(business.Id, null, "Yakın", "Kurye",
                $"5{Random.Shared.NextInt64(100000000, 999999999)}");
            courier.SetAvailability(CourierAvailability.Available);
            Order nearbyOrder = WaitingOrder(business.Id, nearbyBranch.Id, "NEAR", "Gizli Müşteri",
                "05550001122", "Gizli teslimat adresi");
            Order farOrder = WaitingOrder(business.Id, farBranch.Id, "FAR", "Uzak Müşteri",
                "05550003344", "Uzak teslimat adresi");
            context.AddRange(business, nearbyBranch, farBranch, courier, nearbyOrder, farOrder,
                CourierShift.Start(courier.Id, business.Id, DateTimeOffset.UtcNow.AddHours(-1)),
                CourierLocation.Create(courier.Id, business.Id, 40.9911, 29.0290, 5, null, null,
                    DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
            businessId = business.Id;
            courierId = courier.Id;
            nearbyOrderId = nearbyOrder.Id;
        }

        using HttpRequestMessage request = Authorized(HttpMethod.Get,
            "/api/v1/orders/available?page=1&pageSize=100", Permissions.OrdersClaim);
        request.Headers.Add("X-Test-Permission", Permissions.OrdersRead);
        request.Headers.Add("X-Test-Business", businessId.ToString());
        request.Headers.Add("X-Test-Courier", courierId.ToString());
        HttpResponseMessage response = await _client.SendAsync(request);
        string json = await response.Content.ReadAsStringAsync();
        PagedResponse<AvailableOrderResponse>? page = await response.Content
            .ReadFromJsonAsync<PagedResponse<AvailableOrderResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AvailableOrderResponse item = Assert.Single(Assert.IsType<PagedResponse<AvailableOrderResponse>>(page).Items);
        Assert.Equal(nearbyOrderId, item.Id);
        Assert.Equal("Yakın Şube", item.PickupName);
        Assert.InRange(item.PickupDistanceKm, 0, 1);
        Assert.DoesNotContain("Gizli Müşteri", json);
        Assert.DoesNotContain("05550001122", json);
        Assert.DoesNotContain("Gizli teslimat adresi", json);
        Assert.DoesNotContain("Uzak Müşteri", json);
    }

    [Fact]
    public async Task Courier_regular_order_endpoints_do_not_expose_unassigned_order_pii()
    {
        Guid businessId;
        Guid branchId;
        Guid courierId;
        Guid orderId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Business business = Business.Create($"PII boundary {Guid.NewGuid():N}", $"P{Guid.NewGuid():N}"[..12]);
            Branch branch = Branch.Create(business.Id, "Merkez", "Kadıköy", 40.9909, 29.0283);
            Courier courier = Courier.Create(business.Id, branch.Id, "Test", "Kurye",
                $"5{Random.Shared.NextInt64(100000000, 999999999)}");
            Order order = WaitingOrder(business.Id, branch.Id, "PRIVATE", "Gizli Müşteri",
                "05550001122", "Gizli teslimat adresi");
            context.AddRange(business, branch, courier, order);
            await context.SaveChangesAsync();
            businessId = business.Id;
            branchId = branch.Id;
            courierId = courier.Id;
            orderId = order.Id;
        }

        using HttpRequestMessage listRequest = Authorized(HttpMethod.Get, "/api/v1/orders?pageSize=100",
            Permissions.OrdersRead);
        listRequest.Headers.Add("X-Test-Business", businessId.ToString());
        listRequest.Headers.Add("X-Test-Branch", branchId.ToString());
        listRequest.Headers.Add("X-Test-Courier", courierId.ToString());
        HttpResponseMessage listResponse = await _client.SendAsync(listRequest);
        string listJson = await listResponse.Content.ReadAsStringAsync();

        using HttpRequestMessage detailRequest = Authorized(HttpMethod.Get, $"/api/v1/orders/{orderId}",
            Permissions.OrdersRead);
        detailRequest.Headers.Add("X-Test-Business", businessId.ToString());
        detailRequest.Headers.Add("X-Test-Branch", branchId.ToString());
        detailRequest.Headers.Add("X-Test-Courier", courierId.ToString());
        HttpResponseMessage detailResponse = await _client.SendAsync(detailRequest);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.DoesNotContain("Gizli Müşteri", listJson);
        Assert.DoesNotContain("05550001122", listJson);
        Assert.DoesNotContain("Gizli teslimat adresi", listJson);
        Assert.Equal(HttpStatusCode.Forbidden, detailResponse.StatusCode);
    }

    [Fact]
    public async Task Branch_scoped_user_cannot_access_objects_from_another_branch_by_id()
    {
        Guid businessId;
        Guid ownBranchId;
        Guid otherBranchId;
        Guid otherCourierId;
        Guid otherOrderId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Business business = Business.Create($"Branch scope {Guid.NewGuid():N}", $"B{Guid.NewGuid():N}"[..12]);
            Branch ownBranch = Branch.Create(business.Id, "Kendi Şube", "Kadıköy", 40.99, 29.03);
            Branch otherBranch = Branch.Create(business.Id, "Diğer Şube", "Beşiktaş", 41.04, 29.01);
            Courier otherCourier = Courier.Create(business.Id, otherBranch.Id, "Diğer", "Kurye",
                $"5{Random.Shared.NextInt64(100000000, 999999999)}");
            Order otherOrder = WaitingOrder(business.Id, otherBranch.Id, "OTHER-BRANCH",
                "Gizli Müşteri", "05550001234", "Gizli adres");
            context.AddRange(business, ownBranch, otherBranch, otherCourier, otherOrder,
                CourierLocation.Create(otherCourier.Id, business.Id, 41.041, 29.011, 5, null, null,
                    DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
            businessId = business.Id;
            ownBranchId = ownBranch.Id;
            otherBranchId = otherBranch.Id;
            otherCourierId = otherCourier.Id;
            otherOrderId = otherOrder.Id;
        }

        HttpRequestMessage Scoped(HttpMethod method, string path, string permission)
        {
            HttpRequestMessage request = Authorized(method, path, permission);
            request.Headers.Add("X-Test-Business", businessId.ToString());
            request.Headers.Add("X-Test-Branch", ownBranchId.ToString());
            return request;
        }

        using HttpRequestMessage orderRequest = Scoped(HttpMethod.Get, $"/api/v1/orders/{otherOrderId}",
            Permissions.OrdersRead);
        using HttpRequestMessage branchRequest = Scoped(HttpMethod.Get, $"/api/v1/branches/{otherBranchId}",
            Permissions.BranchesRead);
        using HttpRequestMessage courierRequest = Scoped(HttpMethod.Get, $"/api/v1/couriers/{otherCourierId}",
            Permissions.CouriersRead);
        using HttpRequestMessage locationRequest = Scoped(HttpMethod.Get,
            $"/api/v1/locations/couriers/{otherCourierId}/history", Permissions.LocationsRead);

        HttpResponseMessage[] responses =
        [
            await _client.SendAsync(orderRequest),
            await _client.SendAsync(branchRequest),
            await _client.SendAsync(courierRequest),
            await _client.SendAsync(locationRequest)
        ];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
    }

    [Fact]
    public async Task Courier_queue_requires_a_fresh_location()
    {
        Guid businessId;
        Guid branchId;
        Guid courierId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Business business = Business.Create($"Stale queue {Guid.NewGuid():N}", $"S{Guid.NewGuid():N}"[..12]);
            Branch branch = Branch.Create(business.Id, "Merkez", "Kadıköy", 40.9909, 29.0283);
            Courier courier = Courier.Create(business.Id, branch.Id, "Eski", "Konum",
                $"5{Random.Shared.NextInt64(100000000, 999999999)}");
            context.AddRange(business, branch, courier,
                CourierLocation.Create(courier.Id, business.Id, 40.9911, 29.0290, 5, null, null,
                    DateTimeOffset.UtcNow.AddMinutes(-10)));
            await context.SaveChangesAsync();
            businessId = business.Id;
            branchId = branch.Id;
            courierId = courier.Id;
        }

        using HttpRequestMessage request = Authorized(HttpMethod.Get, "/api/v1/orders/available",
            Permissions.OrdersClaim);
        request.Headers.Add("X-Test-Permission", Permissions.OrdersRead);
        request.Headers.Add("X-Test-Business", businessId.ToString());
        request.Headers.Add("X-Test-Branch", branchId.ToString());
        request.Headers.Add("X-Test-Courier", courierId.ToString());

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Courier_queue_requires_an_active_shift_even_with_a_fresh_location()
    {
        Guid businessId;
        Guid branchId;
        Guid courierId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Business business = Business.Create($"Off shift queue {Guid.NewGuid():N}", $"O{Guid.NewGuid():N}"[..12]);
            Branch branch = Branch.Create(business.Id, "Merkez", "Kadıköy", 40.9909, 29.0283);
            Courier courier = Courier.Create(business.Id, branch.Id, "Mesai", "Dışı",
                $"5{Random.Shared.NextInt64(100000000, 999999999)}");
            context.AddRange(business, branch, courier,
                CourierLocation.Create(courier.Id, business.Id, 40.9911, 29.0290, 5, null, null,
                    DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
            businessId = business.Id;
            branchId = branch.Id;
            courierId = courier.Id;
        }

        using HttpRequestMessage request = Authorized(HttpMethod.Get, "/api/v1/orders/available",
            Permissions.OrdersClaim);
        request.Headers.Add("X-Test-Permission", Permissions.OrdersRead);
        request.Headers.Add("X-Test-Business", businessId.ToString());
        request.Headers.Add("X-Test-Branch", branchId.ToString());
        request.Headers.Add("X-Test-Courier", courierId.ToString());

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Courier_cannot_claim_an_order_outside_the_pickup_radius()
    {
        Guid businessId;
        Guid branchId;
        Guid courierId;
        Guid orderId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Business business = Business.Create($"Far claim {Guid.NewGuid():N}", $"R{Guid.NewGuid():N}"[..12]);
            Branch branch = Branch.Create(business.Id, "Uzak Şube", "Sarıyer", 41.1664, 29.0500);
            Courier courier = Courier.Create(business.Id, branch.Id, "Test", "Kurye",
                $"5{Random.Shared.NextInt64(100000000, 999999999)}");
            courier.SetAvailability(CourierAvailability.Available);
            Order order = WaitingOrder(business.Id, branch.Id, "FAR-CLAIM", "Müşteri", "05550009988", "Adres");
            context.AddRange(business, branch, courier, order,
                CourierShift.Start(courier.Id, business.Id, DateTimeOffset.UtcNow.AddHours(-1)),
                CourierLocation.Create(courier.Id, business.Id, 40.9909, 29.0283, 5, null, null,
                    DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
            businessId = business.Id;
            branchId = branch.Id;
            courierId = courier.Id;
            orderId = order.Id;
        }

        using HttpRequestMessage request = Authorized(HttpMethod.Post, $"/api/v1/orders/{orderId}/claim",
            Permissions.OrdersClaim);
        request.Headers.Add("X-Test-Permission", Permissions.OrdersRead);
        request.Headers.Add("X-Test-Business", businessId.ToString());
        request.Headers.Add("X-Test-Branch", branchId.ToString());
        request.Headers.Add("X-Test-Courier", courierId.ToString());

        HttpResponseMessage response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Orders_read_permission_cannot_claim_package()
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Post, $"/api/v1/orders/{Guid.NewGuid()}/claim", Permissions.OrdersRead);
        HttpResponseMessage response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Order WaitingOrder(Guid businessId, Guid branchId, string externalId,
        string customerName, string customerPhone, string deliveryAddress)
    {
        Guid actorId = Guid.NewGuid();
        Order order = Order.Create(businessId, branchId, externalId, customerName, customerPhone,
            deliveryAddress, OrderSource.Phone, 125m, Guid.NewGuid().ToString("N"), new string('A', 64), actorId);
        order.ChangeStatus(OrderStatus.Confirmed, actorId);
        order.ChangeStatus(OrderStatus.WaitingForCourier, actorId);
        return order;
    }

    [Fact]
    public async Task Orders_read_permission_can_get_operations_report()
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Get,
            "/api/v1/reports/operations?from=2026-09-01&to=2026-09-16",
            Permissions.OrdersRead, platformAdmin: true);
        HttpResponseMessage response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_report_range_returns_bad_request()
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Get,
            "/api/v1/reports/operations?from=2026-09-16&to=2026-09-01",
            Permissions.OrdersRead, platformAdmin: true);
        HttpResponseMessage response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Excel_report_is_a_valid_multi_sheet_workbook()
    {
        using HttpRequestMessage request = Authorized(HttpMethod.Get,
            "/api/v1/reports/operations/export?from=2026-09-01&to=2026-09-16&format=xlsx",
            Permissions.OrdersRead, platformAdmin: true);
        HttpResponseMessage response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using XLWorkbook workbook = new(stream);
        Assert.Equal(["Özet", "Günlük", "Kuryeler", "Şubeler", "Kaynaklar"],
            workbook.Worksheets.Select(x => x.Name).ToArray());
        Assert.Equal("DeliveryOps Operasyon Raporu", workbook.Worksheet("Özet").Cell("A1").GetString());
        Assert.True(workbook.Worksheet("Günlük").Cell("A2").DataType == XLDataType.DateTime);
    }

    [Fact]
    public async Task Business_billing_query_cannot_escape_tenant_scope()
    {
        (Guid ownBusinessId, Guid otherBusinessId) = await AddBusinesses();
        using HttpRequestMessage request = Authorized(HttpMethod.Get,
            $"/api/v1/billing/settings?businessId={otherBusinessId}", Permissions.BillingRead);
        request.Headers.Add("X-Test-Business", ownBusinessId.ToString());

        HttpResponseMessage response = await _client.SendAsync(request);
        BillingSettingsResponse? settings = await response.Content.ReadFromJsonAsync<BillingSettingsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ownBusinessId, settings?.BusinessId);
    }

    [Fact]
    public async Task Platform_can_create_and_finalize_billing_settlement()
    {
        (Guid businessId, _) = await AddBusinesses();
        using HttpRequestMessage create = Authorized(HttpMethod.Post, "/api/v1/billing/settlements",
            Permissions.BillingWrite, platformAdmin: true);
        create.Content = JsonContent.Create(new { businessId, from = "2026-09-01", to = "2026-09-30" });
        HttpResponseMessage createdResponse = await _client.SendAsync(create);
        BillingSettlementResponse? settlement = await createdResponse.Content.ReadFromJsonAsync<BillingSettlementResponse>();

        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        Assert.NotNull(settlement);

        using HttpRequestMessage finalize = Authorized(HttpMethod.Post,
            $"/api/v1/billing/settlements/{settlement!.Id}/finalize", Permissions.BillingWrite,
            platformAdmin: true);
        HttpResponseMessage finalizedResponse = await _client.SendAsync(finalize);
        BillingSettlementResponse? finalized = await finalizedResponse.Content.ReadFromJsonAsync<BillingSettlementResponse>();

        Assert.Equal(HttpStatusCode.OK, finalizedResponse.StatusCode);
        Assert.Equal(1, (int?)finalized?.Status);
        Assert.NotNull(finalized?.FinalizedAtUtc);
        Assert.StartsWith("MUT-202609-", finalized?.DocumentNumber);

        using HttpRequestMessage pdfRequest = Authorized(HttpMethod.Get,
            $"/api/v1/billing/settlements/{settlement.Id}/document?format=pdf",
            Permissions.BillingRead, platformAdmin: true);
        HttpResponseMessage pdfResponse = await _client.SendAsync(pdfRequest);
        byte[] pdf = await pdfResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, pdfResponse.StatusCode);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        using HttpRequestMessage excelRequest = Authorized(HttpMethod.Get,
            $"/api/v1/billing/settlements/{settlement.Id}/document?format=xlsx",
            Permissions.BillingRead, platformAdmin: true);
        HttpResponseMessage excelResponse = await _client.SendAsync(excelRequest);
        await using Stream stream = await excelResponse.Content.ReadAsStreamAsync();
        using XLWorkbook workbook = new(stream);
        Assert.Equal(HttpStatusCode.OK, excelResponse.StatusCode);
        Assert.Equal(finalized!.DocumentNumber, workbook.Worksheet("Mutabakat").Cell("A3").GetString());
    }

    [Fact]
    public async Task Order_creation_consumes_exactly_one_credit_and_blocks_when_empty()
    {
        Guid businessId;
        Guid branchId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Business business = Business.Create($"Credit {Guid.NewGuid():N}", $"C{Guid.NewGuid():N}"[..12]);
            Branch branch = Branch.Create(business.Id, "Merkez", "Test adresi", null, null);
            BusinessCreditAccount account = BusinessCreditAccount.Create(business.Id);
            account.Add(1);
            context.AddRange(business, branch, account);
            await context.SaveChangesAsync();
            businessId = business.Id;
            branchId = branch.Id;
        }

        async Task<HttpResponseMessage> CreateOrder(string externalId)
        {
            using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/v1/orders",
                Permissions.OrdersWrite, platformAdmin: true);
            request.Headers.Add("X-Test-Permission", Permissions.OrdersRead);
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            request.Content = JsonContent.Create(new { businessId, branchId, externalId,
                customerName = "Test Müşteri", customerPhone = "05550000000",
                deliveryAddress = "Test adresi", source = 1, totalAmount = 100m });
            return await _client.SendAsync(request);
        }

        HttpResponseMessage first = await CreateOrder("CREDIT-1");
        HttpResponseMessage second = await CreateOrder("CREDIT-2");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using IServiceScope checkScope = _factory.Services.CreateScope();
        CoreDbContext checkContext = checkScope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(0, (await checkContext.BusinessCreditAccounts.SingleAsync(x => x.BusinessId == businessId)).Balance);
        Assert.Equal(1, await checkContext.CreditTransactions.CountAsync(x => x.BusinessId == businessId));
        Assert.Equal(1, await checkContext.Orders.CountAsync(x => x.BusinessId == businessId));

        Guid orderId = await checkContext.Orders.Where(x => x.BusinessId == businessId).Select(x => x.Id).SingleAsync();
        string refundKey = Guid.NewGuid().ToString();
        async Task<HttpResponseMessage> Refund()
        {
            using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/v1/credits/refunds",
                Permissions.CreditsWrite, platformAdmin: true);
            request.Headers.Add("Idempotency-Key", refundKey);
            request.Content = JsonContent.Create(new { businessId, orderId, description = "Test siparişi iadesi" });
            return await _client.SendAsync(request);
        }
        Assert.Equal(HttpStatusCode.OK, (await Refund()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Refund()).StatusCode);
        using IServiceScope refundCheckScope = _factory.Services.CreateScope();
        CoreDbContext refundCheckContext = refundCheckScope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(1, (await refundCheckContext.BusinessCreditAccounts.SingleAsync(x => x.BusinessId == businessId)).Balance);
        Assert.Equal(1, await refundCheckContext.CreditTransactions.CountAsync(x => x.BusinessId == businessId && x.Type == CreditTransactionType.Refund));
    }

    [Fact]
    public async Task Phone_order_is_idempotent_and_cancellation_refunds_credit_once()
    {
        Guid businessId;
        Guid branchId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Business business = Business.Create($"Idempotency {Guid.NewGuid():N}", $"I{Guid.NewGuid():N}"[..12]);
            Branch branch = Branch.Create(business.Id, "Merkez", "Test adresi", null, null);
            BusinessCreditAccount account = BusinessCreditAccount.Create(business.Id);
            account.Add(2);
            context.AddRange(business, branch, account);
            await context.SaveChangesAsync();
            businessId = business.Id;
            branchId = branch.Id;
        }

        string key = Guid.NewGuid().ToString("N");
        async Task<HttpResponseMessage> Create(string customerName)
        {
            using HttpRequestMessage request = Authorized(HttpMethod.Post, "/api/v1/orders/phone",
                Permissions.OrdersWrite, platformAdmin: true);
            request.Headers.Add("X-Test-Permission", Permissions.OrdersRead);
            request.Headers.Add("Idempotency-Key", key);
            request.Content = JsonContent.Create(new { businessId, branchId, customerName,
                customerPhone = "05550000000", deliveryAddress = "Test adresi", totalAmount = 100m });
            return await _client.SendAsync(request);
        }

        HttpResponseMessage first = await Create("Aynı Müşteri");
        HttpResponseMessage replay = await Create("Aynı Müşteri");
        HttpResponseMessage conflict = await Create("Farklı Müşteri");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Guid orderId = (await first.Content.ReadFromJsonAsync<OrderResponse>())!.Id;

        using HttpRequestMessage cancel = Authorized(HttpMethod.Delete, $"/api/v1/orders/{orderId}",
            Permissions.OrdersWrite, platformAdmin: true);
        cancel.Headers.Add("X-Test-Permission", Permissions.OrdersRead);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(cancel)).StatusCode);

        using IServiceScope checkScope = _factory.Services.CreateScope();
        CoreDbContext check = checkScope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(2, (await check.BusinessCreditAccounts.SingleAsync(x => x.BusinessId == businessId)).Balance);
        Assert.Equal(1, await check.Orders.CountAsync(x => x.BusinessId == businessId));
        Assert.Equal(1, await check.CreditTransactions.CountAsync(x => x.OrderId == orderId && x.Type == CreditTransactionType.OrderConsumption));
        Assert.Equal(1, await check.CreditTransactions.CountAsync(x => x.OrderId == orderId && x.Type == CreditTransactionType.Refund));
    }

    [Fact]
    public async Task Provider_lifecycle_is_idempotent_and_refunds_cancelled_order_once()
    {
        Guid businessId;
        Guid branchId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            Business business = Business.Create($"Provider {Guid.NewGuid():N}", $"P{Guid.NewGuid():N}"[..12]);
            Branch branch = Branch.Create(business.Id, "Merkez", "Test adresi", null, null);
            BusinessCreditAccount account = BusinessCreditAccount.Create(business.Id);
            account.Add(2);
            context.AddRange(business, branch, account);
            await context.SaveChangesAsync();
            businessId = business.Id;
            branchId = branch.Id;
        }

        using HttpRequestMessage create = Internal(HttpMethod.Post, "/api/v1/internal/orders");
        create.Content = JsonContent.Create(new { businessId, branchId, externalId = "YS-LIFECYCLE-1",
            customerName = "Test Müşteri", customerPhone = "05550000000",
            deliveryAddress = "Test adresi", source = 3, totalAmount = 100m });
        HttpResponseMessage created = await _client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        string readyEventId = $"YS-LIFECYCLE-1:READY_FOR_PICKUP:{Guid.NewGuid():N}";
        async Task<HttpResponseMessage> SendProviderEvent(string eventId, string status, string? reason = null)
        {
            using HttpRequestMessage request = Internal(HttpMethod.Post, "/api/v1/internal/orders/provider-event");
            request.Content = JsonContent.Create(new { businessId, externalOrderId = "YS-LIFECYCLE-1",
                source = 3, externalEventId = eventId, providerStatus = status,
                cancellationReason = reason });
            return await _client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.OK, (await SendProviderEvent(readyEventId, "READY_FOR_PICKUP")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendProviderEvent(readyEventId, "READY_FOR_PICKUP")).StatusCode);
        string cancelEventId = $"YS-LIFECYCLE-1:CANCELLED:{Guid.NewGuid():N}";
        HttpResponseMessage cancelled = await SendProviderEvent(cancelEventId, "CANCELLED", "ITEM_UNAVAILABLE");
        HttpResponseMessage duplicateCancellation = await SendProviderEvent(cancelEventId, "CANCELLED", "ITEM_UNAVAILABLE");

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicateCancellation.StatusCode);
        using IServiceScope checkScope = _factory.Services.CreateScope();
        CoreDbContext check = checkScope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Order order = await check.Orders.SingleAsync(x => x.BusinessId == businessId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("ITEM_UNAVAILABLE", order.CancellationReason);
        Assert.Equal(2, (await check.BusinessCreditAccounts.SingleAsync(x => x.BusinessId == businessId)).Balance);
        Assert.Equal(1, await check.CreditTransactions.CountAsync(x => x.OrderId == order.Id &&
            x.Type == CreditTransactionType.Refund));
        Assert.Equal(2, await check.ProviderOrderEventReceipts.CountAsync(x => x.OrderId == order.Id));
    }

    private async Task<(Guid First, Guid Second)> AddBusinesses()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        CoreDbContext context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Business first = Business.Create($"Test {Guid.NewGuid():N}", $"T{Guid.NewGuid():N}"[..12]);
        Business second = Business.Create($"Test {Guid.NewGuid():N}", $"T{Guid.NewGuid():N}"[..12]);
        context.Businesses.AddRange(first, second);
        await context.SaveChangesAsync();
        return (first.Id, second.Id);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string uri, string permission, bool platformAdmin = false)
    {
        HttpRequestMessage request = new(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Test");
        request.Headers.Add("X-Test-User", Guid.NewGuid().ToString());
        request.Headers.Add("X-Test-Permission", permission);
        if (platformAdmin) request.Headers.Add("X-Test-Role", "PlatformAdmin");
        return request;
    }

    private static HttpRequestMessage Internal(HttpMethod method, string uri)
    {
        HttpRequestMessage request = new(method, uri);
        request.Headers.Add("X-DeliveryOps-Internal-Key", "development-integrations-internal-key-2026");
        return request;
    }
}

public sealed class CoreApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"core-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<CoreDbContext>>();
            services.RemoveAll<CoreDbContext>();
            services.RemoveAll<ICoreDbContext>();
            services.RemoveAll<IHostedService>();
            services.RemoveAll<ICourierPresenceStore>();
            services.AddDbContext<CoreDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddScoped<ICoreDbContext>(provider => provider.GetRequiredService<CoreDbContext>());
            services.AddSingleton<ICourierPresenceStore, EmptyPresenceStore>();
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}

public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var userId)) return Task.FromResult(AuthenticateResult.NoResult());
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, userId.ToString())];
        foreach (string? permission in Request.Headers["X-Test-Permission"])
            if (!string.IsNullOrWhiteSpace(permission)) claims.Add(new(Permissions.ClaimType, permission));
        foreach (string? role in Request.Headers["X-Test-Role"])
            if (!string.IsNullOrWhiteSpace(role)) claims.Add(new(ClaimTypes.Role, role));
        if (Request.Headers.TryGetValue("X-Test-Business", out var businessId)) claims.Add(new("business_id", businessId.ToString()));
        if (Request.Headers.TryGetValue("X-Test-Branch", out var branchId)) claims.Add(new("branch_id", branchId.ToString()));
        if (Request.Headers.TryGetValue("X-Test-Courier", out var courierId)) claims.Add(new("courier_id", courierId.ToString()));
        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

public sealed class EmptyPresenceStore : ICourierPresenceStore
{
    public Task SetAsync(CourierLocationSnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyDictionary<Guid, CourierLocationSnapshot>> GetAsync(IEnumerable<Guid> courierIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, CourierLocationSnapshot>>(new Dictionary<Guid, CourierLocationSnapshot>());
}

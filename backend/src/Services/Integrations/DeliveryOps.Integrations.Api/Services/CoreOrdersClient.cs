using System.Net.Http.Json;
using DeliveryOps.Integrations.Api.Domain;

namespace DeliveryOps.Integrations.Api.Services;

public sealed class CoreOrdersClient(HttpClient client)
{
    public async Task<bool> BranchExistsAsync(Guid businessId, Guid branchId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"api/v1/internal/references/businesses/{businessId}/branches/{branchId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<CoreOrderResult> CreateAsync(IntegrationConnection connection, InboundOrderRequest order, CancellationToken cancellationToken)
    {
        int source = connection.Provider switch
        {
            IntegrationProvider.Yemeksepeti => 3,
            IntegrationProvider.Getir => 4,
            IntegrationProvider.Pos => 5,
            IntegrationProvider.Trendyol => 7,
            _ => 6
        };
        object payload = new
        {
            connection.BusinessId, connection.BranchId, ExternalId = order.ExternalOrderId,
            order.CustomerName, order.CustomerPhone, order.DeliveryAddress, Source = source, order.TotalAmount,
            order.DeliveryLatitude, order.DeliveryLongitude, order.DeliveryInstructions,
            DeliveryFulfillment = (int)order.DeliveryFulfillment,
            Payment = order.Payment?.ToCorePayload()
        };
        using HttpResponseMessage response = await client.PostAsJsonAsync("api/v1/internal/orders", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Core API rejected order ({(int)response.StatusCode}): {detail}");
        }
        return await response.Content.ReadFromJsonAsync<CoreOrderResult>(cancellationToken)
               ?? throw new HttpRequestException("Core API returned an empty response.");
    }

    public async Task<CoreOrderResult> ApplyProviderEventAsync(IntegrationConnection connection,
        string externalOrderId, string externalEventId, string providerStatus,
        string? cancellationReason, CancellationToken cancellationToken, InboundPayment? payment = null)
    {
        int source = connection.Provider switch
        {
            IntegrationProvider.Yemeksepeti => 3,
            IntegrationProvider.Getir => 4,
            IntegrationProvider.Pos => 5,
            IntegrationProvider.Trendyol => 7,
            _ => 6
        };
        object payload = new
        {
            connection.BusinessId, ExternalOrderId = externalOrderId, Source = source,
            ExternalEventId = externalEventId, ProviderStatus = providerStatus,
            CancellationReason = cancellationReason,
            Payment = payment?.ToCorePayload()
        };
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "api/v1/internal/orders/provider-event", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Core API rejected provider event ({(int)response.StatusCode}): {detail}");
        }
        return await response.Content.ReadFromJsonAsync<CoreOrderResult>(cancellationToken)
               ?? throw new HttpRequestException("Core API returned an empty provider-event response.");
    }

    public async Task SetIntegrationHealthAsync(IntegrationConnection connection, bool healthy,
        string message, DateTimeOffset checkedAtUtc, CancellationToken cancellationToken)
    {
        object payload = new
        {
            connection.BusinessId,
            ConnectionId = connection.Id,
            ConnectionName = connection.Name,
            Healthy = healthy,
            Message = message,
            CheckedAtUtc = checkedAtUtc
        };
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "api/v1/internal/operational-alerts/integration-health", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Core API rejected integration health ({(int)response.StatusCode}): {detail}");
        }
    }
}

public sealed record InboundOrderRequest(string ExternalOrderId, string CustomerName, string CustomerPhone,
    string DeliveryAddress, decimal TotalAmount, double? DeliveryLatitude = null,
    double? DeliveryLongitude = null, string? DeliveryInstructions = null,
    ProviderDeliveryFulfillment DeliveryFulfillment = ProviderDeliveryFulfillment.MerchantCourier,
    InboundPayment? Payment = null);
/// <summary>Values match Core's PaymentMethod enum.</summary>
public enum InboundPaymentMethod { Online = 1, Cash = 2, Card = 3 }
public sealed record InboundPayment(InboundPaymentMethod Method, bool IsPaid, decimal? Amount = null,
    string? Reference = null, DateTimeOffset? PaidAtUtc = null)
{
    public object ToCorePayload() => new { Method = (int)Method, IsPaid, Amount, Reference, PaidAtUtc };
}
public enum ProviderDeliveryFulfillment { MerchantCourier = 0, ProviderCourier = 1, CustomerPickup = 2 }
public sealed record CoreOrderResult(Guid Id, bool Duplicate);

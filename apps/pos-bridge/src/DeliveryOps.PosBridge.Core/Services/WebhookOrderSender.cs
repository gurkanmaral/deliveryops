using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DeliveryOps.PosBridge.Core.Abstractions;
using DeliveryOps.PosBridge.Core.Models;

namespace DeliveryOps.PosBridge.Core.Services;

public sealed class WebhookOrderSender(HttpClient client) : IOrderSender
{
    public async Task<OrderSendResult> SendAsync(BridgeRuntimeSettings settings, PosOrder order, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, settings.WebhookUrl)
        {
            Content = JsonContent.Create(order)
        };
        request.Headers.Add("X-DeliveryOps-Key", settings.Secret);
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                WebhookResponse? result = await response.Content.ReadFromJsonAsync<WebhookResponse>(cancellationToken);
                return OrderSendResult.Sent(result?.OrderId, result?.Duplicate ?? false);
            }
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            bool retryable = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            return OrderSendResult.Failed($"Webhook returned {(int)response.StatusCode}: {ExtractProblem(body)}", retryable);
        }
        catch (HttpRequestException exception)
        {
            return OrderSendResult.Failed(exception.Message, true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OrderSendResult.Failed("Webhook request timed out.", true);
        }
    }

    private static string ExtractProblem(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("detail", out JsonElement detail)) return detail.GetString() ?? body;
            if (document.RootElement.TryGetProperty("title", out JsonElement title)) return title.GetString() ?? body;
        }
        catch (JsonException) { }
        return body.Length > 500 ? body[..500] : body;
    }

    private sealed record WebhookResponse(Guid? OrderId, bool Duplicate);
}

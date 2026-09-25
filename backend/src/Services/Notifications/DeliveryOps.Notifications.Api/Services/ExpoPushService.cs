using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DeliveryOps.Notifications.Api.Domain;

namespace DeliveryOps.Notifications.Api.Services;

public sealed record PushContent(string Title, string Body, IReadOnlyDictionary<string, string> Data);
public sealed record PushTicketResult(string ExpoPushToken, string TicketId);
public sealed record PushSendResult(IReadOnlySet<string> InvalidTokens, IReadOnlyList<PushTicketResult> Tickets);
public sealed record PushReceiptResult(string Status, string? Error);

public sealed class ExpoPushService(HttpClient httpClient, ILogger<ExpoPushService> logger)
{
    public async Task<PushSendResult> SendAsync(IReadOnlyList<PushDevice> devices, PushContent content,
        CancellationToken cancellationToken)
    {
        HashSet<string> invalidTokens = [];
        List<PushTicketResult> tickets = [];
        foreach (PushDevice[] chunk in devices.Chunk(100))
        {
            ExpoPushMessage[] messages = chunk.Select(device => new ExpoPushMessage(device.ExpoPushToken,
                content.Title, content.Body, content.Data)).ToArray();
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync("--/api/v2/push/send", messages, cancellationToken);
            response.EnsureSuccessStatusCode();
            ExpoPushResponse? result = await response.Content.ReadFromJsonAsync<ExpoPushResponse>(cancellationToken);
            if (result?.Data is null) continue;
            for (int index = 0; index < Math.Min(chunk.Length, result.Data.Count); index++)
            {
                ExpoPushTicket ticket = result.Data[index];
                if (ticket.Status == "error")
                    logger.LogWarning("Expo push rejected token for courier {CourierId}: {Error} {Message}",
                        chunk[index].CourierId, ticket.Details?.Error, ticket.Message);
                if (ticket.Details?.Error == "DeviceNotRegistered") invalidTokens.Add(chunk[index].ExpoPushToken);
                if (ticket.Status == "ok" && !string.IsNullOrWhiteSpace(ticket.Id))
                    tickets.Add(new PushTicketResult(chunk[index].ExpoPushToken, ticket.Id));
            }
        }
        return new PushSendResult(invalidTokens, tickets);
    }

    public async Task<IReadOnlyDictionary<string, PushReceiptResult>> GetReceiptsAsync(IReadOnlyList<string> ticketIds,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("--/api/v2/push/getReceipts",
            new { ids = ticketIds }, cancellationToken);
        response.EnsureSuccessStatusCode();
        ExpoReceiptsResponse? result = await response.Content.ReadFromJsonAsync<ExpoReceiptsResponse>(cancellationToken);
        return result?.Data.ToDictionary(x => x.Key,
            x => new PushReceiptResult(x.Value.Status, x.Value.Details?.Error))
            ?? new Dictionary<string, PushReceiptResult>();
    }

    private sealed record ExpoPushMessage(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("data")] IReadOnlyDictionary<string, string> Data,
        [property: JsonPropertyName("sound")] string Sound = "default",
        [property: JsonPropertyName("priority")] string Priority = "high",
        [property: JsonPropertyName("channelId")] string ChannelId = "orders");

    private sealed record ExpoPushResponse([property: JsonPropertyName("data")] List<ExpoPushTicket> Data);
    private sealed record ExpoPushTicket(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("details")] ExpoPushDetails? Details);
    private sealed record ExpoPushDetails([property: JsonPropertyName("error")] string? Error);
    private sealed record ExpoReceiptsResponse(
        [property: JsonPropertyName("data")] Dictionary<string, ExpoReceiptTicket> Data);
    private sealed record ExpoReceiptTicket(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("details")] ExpoPushDetails? Details);
}

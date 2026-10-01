using System.Text.Json;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Integrations.Api.Services;

public sealed class InboundEventProcessor(IntegrationsDbContext context, CoreOrdersClient coreOrdersClient,
    GetirFoodClient getirFoodClient, TrendyolGoClient trendyolClient, IConfiguration configuration,
    TimeProvider timeProvider, ILogger<InboundEventProcessor> logger)
{
    private const int MaxAttempts = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task ProcessAsync(Guid eventId, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({$"integration-event:{eventId}"}, 0))", cancellationToken);
        InboundOrderEvent? inboundEvent = await context.InboundOrderEvents.Include(x => x.Connection)
            .SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (inboundEvent is null || inboundEvent.Status is InboundEventStatus.Completed or InboundEventStatus.DeadLettered)
            return;
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (inboundEvent.Status == InboundEventStatus.Processing &&
            inboundEvent.LastAttemptAtUtc > now.AddMinutes(-2)) return;
        if (!inboundEvent.Connection.IsActive)
        {
            inboundEvent.Fail("Entegrasyon bağlantısı pasif.", now, MaxAttempts);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        inboundEvent.StartProcessing(now);
        await context.SaveChangesAsync(cancellationToken);
        try
        {
            InboundOrderRequest order = JsonSerializer.Deserialize<InboundOrderRequest>(
                inboundEvent.NormalizedPayload, JsonOptions)
                ?? throw new InvalidOperationException("Normalize sipariş gövdesi okunamadı.");
            CoreOrderResult result;
            string? providerStatus = MapProviderStatus(inboundEvent.EventType);
            if (providerStatus is not null)
            {
                result = await coreOrdersClient.ApplyProviderEventAsync(inboundEvent.Connection,
                    inboundEvent.ExternalOrderId, inboundEvent.ExternalEventId, providerStatus,
                    ReadCancellationReason(inboundEvent.RawPayload), cancellationToken, order.Payment);
            }
            else
            {
                result = await coreOrdersClient.CreateAsync(inboundEvent.Connection, order, cancellationToken);
                await AcceptOnProviderAsync(inboundEvent, cancellationToken);
            }
            inboundEvent.Complete(result.Id, timeProvider.GetUtcNow());
        }
        // Any failure (not just the expected HTTP/JSON ones) must be recorded, otherwise the event stays in
        // Processing, is re-picked every two minutes and never reaches the dead-letter limit.
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            inboundEvent.Fail(exception.Message, timeProvider.GetUtcNow(), MaxAttempts);
            logger.LogWarning(exception, "Inbound event {EventId} attempt {Attempt} failed.", eventId, inboundEvent.Attempts);
        }
        await context.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    private static string? MapProviderStatus(string eventType) => eventType.ToLowerInvariant() switch
    {
        "order.ready_for_pickup" or "order.picking" or "order.invoiced" => "READY_FOR_PICKUP",
        "order.dispatched" or "order.shipped" => "DISPATCHED",
        "order.delivered" => "DELIVERED",
        "order.cancelled" or "order.canceled" or "order.unsupplied" => "CANCELLED",
        "order.paid" => "PAID",
        _ => null
    };

    /// <summary>
    /// Getir requires every order to be answered within 30 seconds (otherwise the restaurant is called, and
    /// after 5 minutes it is closed and its orders cancelled), so a Getir order is verified as soon as it
    /// exists in DeliveryOps. A failure fails the event; the retry finds the order already created and only
    /// repeats the verification.
    /// </summary>
    private async Task AcceptOnProviderAsync(InboundOrderEvent inboundEvent, CancellationToken cancellationToken)
    {
        IntegrationConnection connection = inboundEvent.Connection;
        if (connection.Provider == IntegrationProvider.Trendyol)
        {
            await AcceptOnTrendyolAsync(inboundEvent, cancellationToken);
            return;
        }
        if (connection.Provider != IntegrationProvider.Getir) return;
        if (!connection.CredentialsConfigured)
        {
            logger.LogWarning("Getir order {OrderId} could not be verified: connection {ConnectionId} has no Getir keys.",
                inboundEvent.ExternalOrderId, connection.Id);
            return;
        }
        (bool scheduled, int? status) = ReadGetirSchedule(inboundEvent.RawPayload);
        // 325 = scheduled order awaiting approval; 400 = immediate or already pre-approved scheduled order.
        bool useScheduledVerify = scheduled && status == 325;
        try
        {
            await getirFoodClient.VerifyAsync(connection, inboundEvent.ExternalOrderId, useScheduledVerify,
                cancellationToken);
        }
        catch (GetirOrderCancelledException)
        {
            logger.LogInformation("Getir order {OrderId} was cancelled before it could be verified.",
                inboundEvent.ExternalOrderId);
        }
    }

    /// <summary>Trendyol orders stay "Created" until the restaurant accepts them (picked).</summary>
    private async Task AcceptOnTrendyolAsync(InboundOrderEvent inboundEvent, CancellationToken cancellationToken)
    {
        IntegrationConnection connection = inboundEvent.Connection;
        if (!connection.CredentialsConfigured)
        {
            logger.LogWarning("Trendyol order {OrderId} could not be accepted: connection {ConnectionId} has no API keys.",
                inboundEvent.ExternalOrderId, connection.Id);
            return;
        }
        (string? packageId, int preparationTime) = ReadTrendyolPackage(inboundEvent.RawPayload);
        if (packageId is null) return;
        int minutes = preparationTime > 0
            ? preparationTime
            : Math.Clamp(configuration.GetValue("Trendyol:DefaultPreparationMinutes", 20), 5, 120);
        try { await trendyolClient.AcceptAsync(connection, packageId, minutes, cancellationToken); }
        catch (TrendyolPackageCancelledException)
        {
            logger.LogInformation("Trendyol package {PackageId} was cancelled before it could be accepted.", packageId);
        }
    }

    public static (string? PackageId, int PreparationTime) ReadTrendyolPackage(string rawPayload)
    {
        using JsonDocument document = JsonDocument.Parse(rawPayload);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array &&
            content.GetArrayLength() == 1)
            root = content[0];
        string? id = root.TryGetProperty("id", out JsonElement value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
            : null;
        int minutes = root.TryGetProperty("preparationTime", out JsonElement time) && time.TryGetInt32(out int parsed)
            ? parsed : 0;
        return (string.IsNullOrWhiteSpace(id) ? null : id, minutes);
    }

    private static (bool Scheduled, int? Status) ReadGetirSchedule(string rawPayload)
    {
        using JsonDocument document = JsonDocument.Parse(rawPayload);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("foodOrder", out JsonElement wrapped) && wrapped.ValueKind == JsonValueKind.Object)
            root = wrapped;
        bool scheduled = root.TryGetProperty("isScheduled", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;
        int? status = root.TryGetProperty("status", out JsonElement value) && value.TryGetInt32(out int parsed)
            ? parsed : null;
        return (scheduled, status);
    }

    private static string? ReadCancellationReason(string rawPayload)
    {
        using JsonDocument document = JsonDocument.Parse(rawPayload);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("foodOrder", out JsonElement getirOrder) && getirOrder.ValueKind == JsonValueKind.Object)
            root = getirOrder;
        // Getir: cancelReason.messages.tr, then the free-text cancelNote.
        if (root.TryGetProperty("cancelReason", out JsonElement getirReason) && getirReason.ValueKind == JsonValueKind.Object &&
            getirReason.TryGetProperty("messages", out JsonElement messages) && messages.ValueKind == JsonValueKind.Object &&
            messages.TryGetProperty("tr", out JsonElement turkish) && turkish.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(turkish.GetString()))
            return turkish.GetString()!.Trim();
        if (root.TryGetProperty("cancelNote", out JsonElement note) && note.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(note.GetString()))
            return note.GetString()!.Trim();
        JsonElement cancellation;
        if ((!root.TryGetProperty("cancellation", out cancellation) || cancellation.ValueKind != JsonValueKind.Object) &&
            (!root.TryGetProperty("cancelInfo", out cancellation) || cancellation.ValueKind != JsonValueKind.Object))
            return null;
        if (!cancellation.TryGetProperty("reason", out JsonElement reason) ||
            reason.ValueKind != JsonValueKind.String) return null;
        return reason.GetString()?.Trim();
    }
}

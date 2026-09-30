using System.Text.Json;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Integrations.Api.Services;

public sealed class InboundEventProcessor(IntegrationsDbContext context, CoreOrdersClient coreOrdersClient,
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

    private static string? ReadCancellationReason(string rawPayload)
    {
        using JsonDocument document = JsonDocument.Parse(rawPayload);
        JsonElement root = document.RootElement;
        JsonElement cancellation;
        if ((!root.TryGetProperty("cancellation", out cancellation) || cancellation.ValueKind != JsonValueKind.Object) &&
            (!root.TryGetProperty("cancelInfo", out cancellation) || cancellation.ValueKind != JsonValueKind.Object))
            return null;
        if (!cancellation.TryGetProperty("reason", out JsonElement reason) ||
            reason.ValueKind != JsonValueKind.String) return null;
        return reason.GetString()?.Trim();
    }
}

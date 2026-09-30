using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DeliveryOps.Integrations.Api.Services;

public sealed class OutboundEventProcessor(IntegrationsDbContext context,
    YemeksepetiPartnerClient yemeksepetiClient, GetirFoodClient getirClient, TrendyolGoClient trendyolClient,
    IConfiguration configuration, TimeProvider timeProvider, ILogger<OutboundEventProcessor> logger)
{
    private const int MaxAttempts = 5;

    public async Task ProcessAsync(Guid eventId, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({$"outbound-event:{eventId}"}, 0))", cancellationToken);
        OutboundOrderEvent? item = await context.OutboundOrderEvents.Include(x => x.Connection)
            .SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (item is null || item.Status is OutboundEventStatus.Completed or OutboundEventStatus.DeadLettered) return;
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (item.Status == OutboundEventStatus.Processing && item.LastAttemptAtUtc > now.AddMinutes(-2)) return;
        item.StartProcessing(now);
        await context.SaveChangesAsync(cancellationToken);
        try
        {
            if (!item.Connection.IsActive) throw new InvalidOperationException("Entegrasyon bağlantısı pasif.");
            if (item.Connection.Provider is IntegrationProvider.Getir or IntegrationProvider.Trendyol)
            {
                if (item.Connection.Provider == IntegrationProvider.Getir)
                    await ApplyGetirStatusAsync(item, cancellationToken);
                else
                    await ApplyTrendyolStatusAsync(item, cancellationToken);
                item.Complete(timeProvider.GetUtcNow());
                await context.SaveChangesAsync(CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
                return;
            }
            // An order accumulates several inbound events (re-sent RECEIVED webhooks, provider lifecycle
            // updates), all linked to the same core order; use the most recent full payload.
            InboundOrderEvent inbound = await context.InboundOrderEvents.AsNoTracking()
                .Where(x => x.ConnectionId == item.ConnectionId && x.CoreOrderId == item.CoreOrderId)
                .OrderByDescending(x => x.ProcessedAtUtc)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Siparişin kaynak entegrasyon olayı bulunamadı.");
            await yemeksepetiClient.UpdateOrderStatusAsync(item.Connection, item.ExternalOrderId,
                item.ProviderStatus, item.CancellationReason, inbound.RawPayload, cancellationToken);
            item.Complete(timeProvider.GetUtcNow());
        }
        // Any failure must be recorded so the event backs off and eventually dead-letters instead of looping.
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            item.Fail(exception.Message, timeProvider.GetUtcNow(), MaxAttempts);
            logger.LogWarning(exception, "Outbound event {EventId} attempt {Attempt} failed.", item.Id, item.Attempts);
        }
        await context.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    /// <summary>
    /// Trendyol only accepts forward steps (invoiced → manual-shipped → manual-delivered), so the earlier
    /// steps are repeated first; a step the package already passed is reconciled as success by the client.
    /// </summary>
    private async Task ApplyTrendyolStatusAsync(OutboundOrderEvent item, CancellationToken cancellationToken)
    {
        if (!item.Connection.CredentialsConfigured)
        {
            logger.LogWarning("Trendyol {Status} for order {OrderId} not sent: connection {ConnectionId} has no API keys.",
                item.ProviderStatus, item.ExternalOrderId, item.ConnectionId);
            return;
        }
        List<string> payloads = await context.InboundOrderEvents.AsNoTracking()
            .Where(x => x.ConnectionId == item.ConnectionId && x.ExternalOrderId == item.ExternalOrderId)
            .OrderBy(x => x.ReceivedAtUtc)
            .Select(x => x.RawPayload).ToListAsync(cancellationToken);
        string packageId = payloads.Select(x => InboundEventProcessor.ReadTrendyolPackage(x).PackageId)
                               .FirstOrDefault(x => x is not null)
                           ?? throw new InvalidOperationException("Trendyol paket numarası bulunamadı.");
        try
        {
            switch (item.ProviderStatus)
            {
                case Controllers.TrendyolStatuses.Unsupplied:
                    List<string> itemIds = payloads.Select(ReadTrendyolItemIds).FirstOrDefault(x => x.Count > 0) ?? [];
                    if (itemIds.Count == 0) throw new InvalidOperationException("Trendyol paket ürünleri bulunamadı.");
                    // 623 = "Mağaza siparişi hazırlayamıyor", the restaurant-side reason valid for every model.
                    int reasonId = configuration.GetValue("Trendyol:CancelReasonId", 623);
                    await trendyolClient.UnsupplyAsync(item.Connection, packageId, itemIds, reasonId, cancellationToken);
                    break;
                case Controllers.TrendyolStatuses.Invoiced:
                    await trendyolClient.InvoiceAsync(item.Connection, packageId, cancellationToken);
                    break;
                case Controllers.TrendyolStatuses.Shipped:
                    await trendyolClient.InvoiceAsync(item.Connection, packageId, cancellationToken);
                    await trendyolClient.ShipAsync(item.Connection, packageId, cancellationToken);
                    break;
                case Controllers.TrendyolStatuses.Delivered:
                    await trendyolClient.InvoiceAsync(item.Connection, packageId, cancellationToken);
                    await trendyolClient.ShipAsync(item.Connection, packageId, cancellationToken);
                    await trendyolClient.DeliverAsync(item.Connection, packageId, cancellationToken);
                    break;
            }
        }
        catch (TrendyolPackageCancelledException exception)
        {
            logger.LogInformation(exception, "Skipping Trendyol {Status} for cancelled package {PackageId}.",
                item.ProviderStatus, packageId);
        }
    }

    /// <summary>Every not-yet-cancelled packageItemId: a full cancellation must list all of them.</summary>
    public static List<string> ReadTrendyolItemIds(string rawPayload)
    {
        List<string> ids = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayload);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array &&
                content.GetArrayLength() == 1)
                root = content[0];
            if (!root.TryGetProperty("lines", out JsonElement lines) || lines.ValueKind != JsonValueKind.Array) return ids;
            foreach (JsonElement line in lines.EnumerateArray())
            {
                if (!line.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array) continue;
                foreach (JsonElement packageItem in items.EnumerateArray())
                {
                    if (packageItem.TryGetProperty("isCancelled", out JsonElement cancelled) &&
                        cancelled.ValueKind == JsonValueKind.True) continue;
                    if (!packageItem.TryGetProperty("packageItemId", out JsonElement id)) continue;
                    string value = id.ValueKind == JsonValueKind.String ? id.GetString() ?? string.Empty : id.GetRawText();
                    if (!string.IsNullOrWhiteSpace(value)) ids.Add(value);
                }
            }
        }
        catch (JsonException) { }
        return ids;
    }

    /// <summary>
    /// Getir requires prepare before handover/deliver (and at least a minute between steps). Prepare is
    /// repeated first so a skipped "ready" step does not block the final call; too-early calls fail and
    /// are retried with backoff until Getir accepts them.
    /// </summary>
    private async Task ApplyGetirStatusAsync(OutboundOrderEvent item, CancellationToken cancellationToken)
    {
        if (!item.Connection.CredentialsConfigured)
        {
            // Same rule as inbound verify: without Getir keys (e.g. simulator connections) there is nothing to call.
            logger.LogWarning("Getir {Status} for order {OrderId} not sent: connection {ConnectionId} has no Getir keys.",
                item.ProviderStatus, item.ExternalOrderId, item.ConnectionId);
            return;
        }
        try
        {
            await getirClient.PrepareAsync(item.Connection, item.ExternalOrderId, cancellationToken);
            if (item.ProviderStatus == Controllers.GetirStatuses.Handover)
                await getirClient.HandoverAsync(item.Connection, item.ExternalOrderId, cancellationToken);
            else if (item.ProviderStatus == Controllers.GetirStatuses.Deliver)
                await getirClient.DeliverAsync(item.Connection, item.ExternalOrderId, cancellationToken);
        }
        catch (GetirOrderCancelledException exception)
        {
            // Nothing left to report for an order Getir already cancelled.
            logger.LogInformation(exception, "Skipping Getir {Status} for cancelled order {OrderId}.",
                item.ProviderStatus, item.ExternalOrderId);
        }
    }
}

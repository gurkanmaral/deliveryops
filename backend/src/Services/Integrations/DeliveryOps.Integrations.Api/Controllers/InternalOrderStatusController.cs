using System.Text.Json;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using DeliveryOps.Integrations.Api.Security;
using DeliveryOps.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Integrations.Api.Controllers;

[ApiController]
[Route("internal/v1/order-status-events")]
[Authorize(Policy = InternalPermissions.OrderStatusWrite)]
public sealed class InternalOrderStatusController(IntegrationsDbContext context,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult> Receive(CoreOrderStatusEvent request,
        CancellationToken cancellationToken)
    {
        if (request.EventId == Guid.Empty || request.OrderId == Guid.Empty) return NoContent();
        if (request.Source == CoreOrderSource.Getir) return await ReceiveGetirAsync(request, cancellationToken);
        if (request.Source == CoreOrderSource.Trendyol) return await ReceiveTrendyolAsync(request, cancellationToken);
        if (request.Source != CoreOrderSource.Yemeksepeti) return NoContent();
        if (await context.OutboundOrderEvents.AnyAsync(x => x.SourceEventId == request.EventId,
                cancellationToken)) return Accepted();

        InboundOrderEvent? inbound = await context.InboundOrderEvents.AsNoTracking()
            .Include(x => x.Connection)
            .Where(x => x.CoreOrderId == request.OrderId && x.Connection.IsActive &&
                        x.Connection.Provider == IntegrationProvider.Yemeksepeti)
            .OrderByDescending(x => x.ProcessedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (inbound is null)
            return Conflict(new ProblemDetails
            {
                Title = "Kaynak entegrasyon olayı henüz hazır değil.",
                Detail = "Core outbox olayı daha sonra yeniden denenecek.", Status = 409
            });

        string transportType = ReadTransportType(inbound.RawPayload);
        string? providerStatus = MapStatus(request.Status, transportType);
        if (providerStatus is null) return NoContent();

        string lifecycleEventType = $"order.{providerStatus.ToLowerInvariant()}";
        InboundEventStatus? lifecycleStatus = await context.InboundOrderEvents.AsNoTracking()
            .Where(x => x.ConnectionId == inbound.ConnectionId &&
                        x.ExternalOrderId == inbound.ExternalOrderId &&
                        x.EventType == lifecycleEventType)
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Select(x => (InboundEventStatus?)x.Status)
            .FirstOrDefaultAsync(cancellationToken);
        if (lifecycleStatus == InboundEventStatus.Completed) return NoContent();
        if (lifecycleStatus.HasValue)
            return Conflict(new ProblemDetails
            {
                Title = "Sağlayıcı kaynaklı durum olayı henüz tamamlanmadı.",
                Detail = "Echo gönderimi engellemek için Core outbox olayı yeniden denenecek.", Status = 409
            });

        if (await context.OutboundOrderEvents.AnyAsync(x => x.ConnectionId == inbound.ConnectionId &&
                x.ExternalOrderId == inbound.ExternalOrderId && x.ProviderStatus == providerStatus,
                cancellationToken)) return Accepted();

        context.OutboundOrderEvents.Add(OutboundOrderEvent.Create(request.EventId, inbound.ConnectionId,
            request.OrderId, inbound.ExternalOrderId, providerStatus, request.CancellationReason,
            timeProvider.GetUtcNow()));
        await context.SaveChangesAsync(cancellationToken);
        return Accepted();
    }

    private async Task<ActionResult> ReceiveGetirAsync(CoreOrderStatusEvent request, CancellationToken cancellationToken)
    {
        if (await context.OutboundOrderEvents.AnyAsync(x => x.SourceEventId == request.EventId, cancellationToken))
            return Accepted();
        InboundOrderEvent? inbound = await context.InboundOrderEvents.AsNoTracking()
            .Include(x => x.Connection)
            .Where(x => x.CoreOrderId == request.OrderId && x.Connection.IsActive &&
                        x.Connection.Provider == IntegrationProvider.Getir)
            .OrderByDescending(x => x.ProcessedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (inbound is null)
            return Conflict(new ProblemDetails
            {
                Title = "Kaynak entegrasyon olayı henüz hazır değil.",
                Detail = "Core outbox olayı daha sonra yeniden denenecek.", Status = 409
            });
        bool getirCourier = request.DeliveryFulfillment.HasValue
            ? request.DeliveryFulfillment == 1
            : ReadGetirDeliveryType(inbound.RawPayload) == 1;
        string? providerStatus = request.Status switch
        {
            CoreOrderStatus.WaitingForCourier => GetirStatuses.Prepare,
            CoreOrderStatus.Delivered => getirCourier ? GetirStatuses.Handover : GetirStatuses.Deliver,
            _ => null
        };
        if (providerStatus is null) return NoContent();
        if (await context.OutboundOrderEvents.AnyAsync(x => x.ConnectionId == inbound.ConnectionId &&
                x.ExternalOrderId == inbound.ExternalOrderId && x.ProviderStatus == providerStatus, cancellationToken))
            return Accepted();
        context.OutboundOrderEvents.Add(OutboundOrderEvent.Create(request.EventId, inbound.ConnectionId,
            request.OrderId, inbound.ExternalOrderId, providerStatus, null, timeProvider.GetUtcNow()));
        await context.SaveChangesAsync(cancellationToken);
        return Accepted();
    }

    private async Task<ActionResult> ReceiveTrendyolAsync(CoreOrderStatusEvent request, CancellationToken cancellationToken)
    {
        if (await context.OutboundOrderEvents.AnyAsync(x => x.SourceEventId == request.EventId, cancellationToken))
            return Accepted();
        InboundOrderEvent? inbound = await context.InboundOrderEvents.AsNoTracking()
            .Include(x => x.Connection)
            .Where(x => x.CoreOrderId == request.OrderId && x.Connection.IsActive &&
                        x.Connection.Provider == IntegrationProvider.Trendyol)
            .OrderByDescending(x => x.ProcessedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (inbound is null)
            return Conflict(new ProblemDetails
            {
                Title = "Kaynak entegrasyon olayı henüz hazır değil.",
                Detail = "Core outbox olayı daha sonra yeniden denenecek.", Status = 409
            });
        int fulfillment = request.DeliveryFulfillment ?? ReadTrendyolFulfillment(inbound.RawPayload);
        bool ownDelivery = fulfillment != 1;
        string? providerStatus = request.Status switch
        {
            CoreOrderStatus.WaitingForCourier => TrendyolStatuses.Invoiced,
            CoreOrderStatus.PickedUp when fulfillment == 0 => TrendyolStatuses.Shipped,
            CoreOrderStatus.Delivered => ownDelivery ? TrendyolStatuses.Delivered : TrendyolStatuses.Invoiced,
            CoreOrderStatus.Cancelled => TrendyolStatuses.Unsupplied,
            _ => null
        };
        if (providerStatus is null) return NoContent();

        // Do not echo a status Trendyol itself reported (e.g. its own cancellation or automatic invoicing).
        string[] echoTypes = providerStatus switch
        {
            TrendyolStatuses.Invoiced => ["order.invoiced", "order.shipped", "order.delivered"],
            TrendyolStatuses.Shipped => ["order.shipped", "order.delivered"],
            TrendyolStatuses.Delivered => ["order.delivered"],
            _ => ["order.cancelled", "order.unsupplied"]
        };
        List<InboundEventStatus> lifecycle = await context.InboundOrderEvents.AsNoTracking()
            .Where(x => x.ConnectionId == inbound.ConnectionId && x.ExternalOrderId == inbound.ExternalOrderId &&
                        echoTypes.Contains(x.EventType))
            .Select(x => x.Status).ToListAsync(cancellationToken);
        if (lifecycle.Contains(InboundEventStatus.Completed)) return NoContent();
        if (lifecycle.Count > 0)
            return Conflict(new ProblemDetails
            {
                Title = "Sağlayıcı kaynaklı durum olayı henüz tamamlanmadı.",
                Detail = "Echo gönderimi engellemek için Core outbox olayı yeniden denenecek.", Status = 409
            });
        if (await context.OutboundOrderEvents.AnyAsync(x => x.ConnectionId == inbound.ConnectionId &&
                x.ExternalOrderId == inbound.ExternalOrderId && x.ProviderStatus == providerStatus, cancellationToken))
            return Accepted();
        context.OutboundOrderEvents.Add(OutboundOrderEvent.Create(request.EventId, inbound.ConnectionId,
            request.OrderId, inbound.ExternalOrderId, providerStatus, request.CancellationReason,
            timeProvider.GetUtcNow()));
        await context.SaveChangesAsync(cancellationToken);
        return Accepted();
    }

    /// <summary>0 = restaurant courier, 1 = Trendyol courier (GO), 2 = customer pickup.</summary>
    private static int ReadTrendyolFulfillment(string rawPayload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayload);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array &&
                content.GetArrayLength() == 1)
                root = content[0];
            if (root.TryGetProperty("storePickupSelected", out JsonElement pickup) && pickup.ValueKind == JsonValueKind.True)
                return 2;
            string type = root.TryGetProperty("deliveryType", out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : root.TryGetProperty("deliveryModel", out JsonElement model) && model.ValueKind == JsonValueKind.String
                    ? model.GetString() ?? string.Empty : string.Empty;
            return string.Equals(type, "GO", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        catch (JsonException) { return 0; }
    }

    private static int? ReadGetirDeliveryType(string rawPayload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayload);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("foodOrder", out JsonElement wrapped) && wrapped.ValueKind == JsonValueKind.Object)
                root = wrapped;
            return root.TryGetProperty("deliveryType", out JsonElement value) && value.TryGetInt32(out int type)
                ? type : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? MapStatus(CoreOrderStatus status, string transportType) => status switch
    {
        CoreOrderStatus.Cancelled => "CANCELLED",
        CoreOrderStatus.WaitingForCourier when transportType == "LOGISTICS_DELIVERY" => "READY_FOR_PICKUP",
        CoreOrderStatus.PickedUp when transportType != "LOGISTICS_DELIVERY" => "DISPATCHED",
        _ => null
    };

    private static string ReadTransportType(string rawPayload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayload);
            return document.RootElement.TryGetProperty("transport_type", out JsonElement value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()?.ToUpperInvariant() ?? string.Empty : string.Empty;
        }
        catch (JsonException) { return string.Empty; }
    }
}

public enum CoreOrderSource
{
    Phone = 0, AdminPanel = 1, BusinessPanel = 2, Yemeksepeti = 3,
    Getir = 4, Pos = 5, Other = 6, Trendyol = 7
}
public enum CoreOrderStatus { New, Confirmed, WaitingForCourier, Assigned, PickedUp, OnTheWay, Delivered, Cancelled, DeliveryFailed, Returned }
public sealed record CoreOrderStatusEvent(Guid EventId, Guid OrderId, Guid BusinessId, Guid BranchId,
    string ExternalId, CoreOrderSource Source, CoreOrderStatus Status, string? CancellationReason,
    DateTimeOffset OccurredAtUtc, int? DeliveryFulfillment = null);

public static class TrendyolStatuses
{
    public const string Invoiced = "TRENDYOL_INVOICED";
    public const string Shipped = "TRENDYOL_SHIPPED";
    public const string Delivered = "TRENDYOL_DELIVERED";
    public const string Unsupplied = "TRENDYOL_UNSUPPLIED";
}

public static class GetirStatuses
{
    public const string Prepare = "GETIR_PREPARE";
    public const string Handover = "GETIR_HANDOVER";
    public const string Deliver = "GETIR_DELIVER";
}

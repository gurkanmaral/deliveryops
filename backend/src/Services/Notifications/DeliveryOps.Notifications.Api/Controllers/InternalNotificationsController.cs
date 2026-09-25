using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Notifications.Api.Domain;
using DeliveryOps.Notifications.Api.Persistence;
using DeliveryOps.Notifications.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Notifications.Api.Controllers;

[ApiController]
[Route("internal/v1/notifications")]
[Authorize(Policy = InternalPermissions.NotificationsWrite)]
public sealed class InternalNotificationsController(NotificationsDbContext database, ExpoPushService pushService) : ControllerBase
{
    [HttpPost("order-event")]
    public async Task<ActionResult> OrderEvent(OrderNotificationRequest request, CancellationToken cancellationToken)
    {
        if (await database.NotificationReceipts.AnyAsync(x => x.EventId == request.EventId, cancellationToken)) return NoContent();

        IQueryable<PushDevice> query = database.PushDevices.Where(x => x.IsActive && x.BusinessId == request.BusinessId);
        query = request.CourierId.HasValue
            ? query.Where(x => x.CourierId == request.CourierId)
            : query.Where(x => !request.BranchId.HasValue || x.BranchId == null || x.BranchId == request.BranchId);
        List<PushDevice> devices = await query.ToListAsync(cancellationToken);

        string title = request.CourierId.HasValue ? "Yeni paket atandı" : "Paket havuzda";
        string body = request.CourierId.HasValue
            ? "Yeni bir sipariş sana atandı."
            : "Yeni bir paket üstlenilebilir.";
        PushSendResult sendResult = await pushService.SendAsync(devices,
            new PushContent(title, body, new Dictionary<string, string>
            {
                ["url"] = request.CourierId.HasValue ? "/orders" : "/available",
                ["orderId"] = request.OrderId.ToString(),
                ["eventType"] = request.EventType
            }), cancellationToken);
        foreach (PushDevice device in devices.Where(x => sendResult.InvalidTokens.Contains(x.ExpoPushToken))) device.Deactivate();
        database.ExpoPushReceipts.AddRange(sendResult.Tickets.Select(ticket =>
            ExpoPushReceipt.Create(ticket.TicketId, ticket.ExpoPushToken, DateTimeOffset.UtcNow)));
        database.NotificationReceipts.Add(NotificationReceipt.Create(request.EventId, request.EventType, devices.Count));
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record OrderNotificationRequest(Guid EventId, string EventType, Guid OrderId, Guid BusinessId,
    Guid? BranchId, Guid? CourierId);

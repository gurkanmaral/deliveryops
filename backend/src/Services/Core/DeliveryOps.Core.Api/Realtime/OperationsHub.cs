using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DeliveryOps.Core.Api.Realtime;

[Authorize]
public sealed class OperationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        string? businessId = Context.User?.FindFirst("business_id")?.Value;
        string? courierId = Context.User?.FindFirst("courier_id")?.Value;
        if (!string.IsNullOrWhiteSpace(courierId))
        {
            // Couriers must not join the business group: it carries every order with customer PII and every
            // courier's position. They get their own orders plus a PII-free pool signal for the available list.
            await Groups.AddToGroupAsync(Context.ConnectionId, $"courier:{courierId}");
            if (!string.IsNullOrWhiteSpace(businessId))
                await Groups.AddToGroupAsync(Context.ConnectionId, CourierPoolGroup(businessId));
        }
        else if (!string.IsNullOrWhiteSpace(businessId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"business:{businessId}");
        }
        if (Context.User?.IsInRole("PlatformAdmin") == true) await Groups.AddToGroupAsync(Context.ConnectionId, "platform");
        await base.OnConnectedAsync();
    }

    public static string CourierPoolGroup(object businessId) => $"business-couriers:{businessId}";
}

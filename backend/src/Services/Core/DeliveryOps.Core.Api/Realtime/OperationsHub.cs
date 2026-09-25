using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DeliveryOps.Core.Api.Realtime;

[Authorize]
public sealed class OperationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        string? businessId = Context.User?.FindFirst("business_id")?.Value;
        if (!string.IsNullOrWhiteSpace(businessId)) await Groups.AddToGroupAsync(Context.ConnectionId, $"business:{businessId}");
        string? courierId = Context.User?.FindFirst("courier_id")?.Value;
        if (!string.IsNullOrWhiteSpace(courierId)) await Groups.AddToGroupAsync(Context.ConnectionId, $"courier:{courierId}");
        if (Context.User?.IsInRole("PlatformAdmin") == true) await Groups.AddToGroupAsync(Context.ConnectionId, "platform");
        await base.OnConnectedAsync();
    }
}

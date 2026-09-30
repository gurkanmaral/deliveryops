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
            string? branchId = Context.User?.FindFirst("branch_id")?.Value;
            if (string.IsNullOrWhiteSpace(branchId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"business:{businessId}");
            }
            else
            {
                // Branch-scoped staff see only their branch's orders (customer data), matching the REST API,
                // plus the business-wide courier map and alerts.
                await Groups.AddToGroupAsync(Context.ConnectionId, BranchOrdersGroup(branchId));
                await Groups.AddToGroupAsync(Context.ConnectionId, BranchOperationsGroup(businessId));
            }
        }
        if (Context.User?.IsInRole("PlatformAdmin") == true) await Groups.AddToGroupAsync(Context.ConnectionId, "platform");
        await base.OnConnectedAsync();
    }

    public static string CourierPoolGroup(object businessId) => $"business-couriers:{businessId}";
    public static string BranchOrdersGroup(object branchId) => $"branch:{branchId}";
    public static string BranchOperationsGroup(object businessId) => $"business-branch-staff:{businessId}";
}

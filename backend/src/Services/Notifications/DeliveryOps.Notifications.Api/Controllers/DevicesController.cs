using System.Security.Claims;
using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Notifications.Api.Domain;
using DeliveryOps.Notifications.Api.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Notifications.Api.Controllers;

[ApiController]
[Route("api/v1/devices")]
[Authorize]
public sealed class DevicesController(NotificationsDbContext database) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<PagedResponse<DeviceResponse>>> Me(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryClaims(out Guid userId, out _, out _, out _)) return Forbid();
        IQueryable<PushDevice> query = database.PushDevices.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive);
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        int totalCount = await query.CountAsync(cancellationToken);
        List<DeviceResponse> items = await query.OrderByDescending(x => x.UpdatedAtUtc)
            .ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new DeviceResponse(x.Id, x.Platform, x.DeviceName, x.CreatedAtUtc, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(new PagedResponse<DeviceResponse>(items, page, pageSize, totalCount));
    }

    [HttpPost]
    public async Task<ActionResult<DeviceResponse>> Register(RegisterDeviceRequest request, CancellationToken cancellationToken)
    {
        if (!TryClaims(out Guid userId, out Guid courierId, out Guid businessId, out Guid? branchId)) return Forbid();
        string token = request.ExpoPushToken.Trim();
        if (!IsExpoPushToken(token)) return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["expoPushToken"] = ["Geçerli bir Expo push token gönderin."] }));
        if (request.Platform.Trim().ToLowerInvariant() is not ("ios" or "android"))
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["platform"] = ["Platform ios veya android olmalıdır."] }));

        PushDevice? device = await database.PushDevices.SingleOrDefaultAsync(x => x.ExpoPushToken == token, cancellationToken);
        if (device is null)
        {
            device = PushDevice.Create(userId, courierId, businessId, branchId, token, request.Platform, request.DeviceName);
            database.PushDevices.Add(device);
        }
        else device.Register(userId, courierId, businessId, branchId, request.Platform, request.DeviceName);
        await database.SaveChangesAsync(cancellationToken);
        return Ok(new DeviceResponse(device.Id, device.Platform, device.DeviceName, device.CreatedAtUtc, device.UpdatedAtUtc));
    }

    [HttpDelete]
    public async Task<ActionResult> Unregister(UnregisterDeviceRequest request, CancellationToken cancellationToken)
    {
        if (!TryClaims(out Guid userId, out _, out _, out _)) return Forbid();
        PushDevice? device = await database.PushDevices.SingleOrDefaultAsync(
            x => x.UserId == userId && x.ExpoPushToken == request.ExpoPushToken.Trim(), cancellationToken);
        if (device is not null) { device.Deactivate(); await database.SaveChangesAsync(cancellationToken); }
        return NoContent();
    }

    private bool TryClaims(out Guid userId, out Guid courierId, out Guid businessId, out Guid? branchId)
    {
        bool hasUser = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        bool hasCourier = Guid.TryParse(User.FindFirstValue("courier_id"), out courierId);
        bool hasBusiness = Guid.TryParse(User.FindFirstValue("business_id"), out businessId);
        branchId = Guid.TryParse(User.FindFirstValue("branch_id"), out Guid branch) ? branch : null;
        return hasUser && hasCourier && hasBusiness;
    }

    private static bool IsExpoPushToken(string value) =>
        (value.StartsWith("ExpoPushToken[") || value.StartsWith("ExponentPushToken[")) && value.EndsWith(']') && value.Length <= 255;
}

public sealed record RegisterDeviceRequest(string ExpoPushToken, string Platform, string? DeviceName);
public sealed record UnregisterDeviceRequest(string ExpoPushToken);
public sealed record DeviceResponse(Guid Id, string Platform, string? DeviceName, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

using DeliveryOps.Core.Api.Security;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Operations;
using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.Core.Queries.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/internal/operational-alerts")]
[Authorize(Policy = InternalPermissions.OperationalAlertsWrite)]
public sealed class InternalOperationalAlertsController(
    CoreDbContext context,
    IOperationalAlertNotifier notifier,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("integration-health")]
    public async Task<ActionResult> SetIntegrationHealth(InternalIntegrationHealthRequest request,
        CancellationToken cancellationToken)
    {
        if (request.BusinessId == Guid.Empty || request.ConnectionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ConnectionName) || string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new ProblemDetails
                { Title = "İşletme, bağlantı ve sağlık mesajı zorunludur.", Status = 400 });
        if (!await context.Businesses.AsNoTracking().AnyAsync(x =>
                x.Id == request.BusinessId && x.IsActive, cancellationToken))
            return NotFound(new ProblemDetails { Title = "Aktif işletme bulunamadı.", Status = 404 });

        string alertKey = $"integration:{request.ConnectionId}:health";
        DateTimeOffset now = timeProvider.GetUtcNow();
        OperationalAlert? changedAlert = null;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({alertKey}, 0))", cancellationToken);
        OperationalAlert? alert = await context.OperationalAlerts.SingleOrDefaultAsync(
            x => x.AlertKey == alertKey, cancellationToken);

        if (request.Healthy)
        {
            if (alert is not null && alert.Resolve(now)) changedAlert = alert;
        }
        else
        {
            string connectionName = Truncate(request.ConnectionName.Trim(), 120);
            string title = Truncate($"{connectionName} bağlantısı çalışmıyor", 180);
            string message = Truncate(request.Message.Trim(), 500);
            if (alert is null)
            {
                alert = OperationalAlert.Create(request.BusinessId, null, null, alertKey,
                    OperationalAlertType.IntegrationConnectionUnavailable,
                    OperationalAlertSeverity.Critical, title, message, now);
                context.OperationalAlerts.Add(alert);
                changedAlert = alert;
            }
            else if (alert.Refresh(OperationalAlertSeverity.Critical, title, message, now))
            {
                changedAlert = alert;
            }
        }

        if (changedAlert is not null) await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (changedAlert is not null)
            await notifier.AlertChangedAsync(OperationalAlertMapper.Map(changedAlert), cancellationToken);
        return NoContent();
    }

    private static string Truncate(string value, int maxLength) =>
        value[..Math.Min(value.Length, maxLength)];
}

public sealed record InternalIntegrationHealthRequest(Guid BusinessId, Guid ConnectionId,
    string ConnectionName, bool Healthy, string Message, DateTimeOffset CheckedAtUtc);

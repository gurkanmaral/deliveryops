using System.Text.Json;
using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.Core.Queries.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/operations/notification-outbox")]
public sealed class NotificationOutboxController(CoreDbContext context, IRequestContext requestContext,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.IntegrationsRead)]
    public async Task<ActionResult<PagedResponse<NotificationOutboxItemResponse>>> Get(
        [FromQuery] bool deadLetteredOnly = true, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        IQueryable<NotificationOutboxMessage> query = context.NotificationOutbox.AsNoTracking();
        if (!requestContext.IsPlatformAdmin)
        {
            if (!requestContext.BusinessId.HasValue) return Forbid();
            query = query.Where(x => x.BusinessId == requestContext.BusinessId.Value);
        }
        if (deadLetteredOnly) query = query.Where(x => x.DeadLetteredAtUtc != null);
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        int totalCount = await query.CountAsync(cancellationToken);
        List<NotificationOutboxItemResponse> items = await query
            .OrderByDescending(x => x.DeadLetteredAtUtc ?? x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new NotificationOutboxItemResponse(x.Id, x.BusinessId, x.OrderId, x.EventType,
                x.Attempts, x.LastHttpStatusCode, x.LastError, x.CreatedAtUtc, x.NextAttemptAtUtc,
                x.ProcessedAtUtc, x.DeadLetteredAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(new PagedResponse<NotificationOutboxItemResponse>(items, page, pageSize, totalCount));
    }

    [HttpPost("{id:guid}/retry")]
    [Authorize(Policy = Permissions.IntegrationsWrite)]
    public async Task<ActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        NotificationOutboxMessage? message = await context.NotificationOutbox.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (message is null) return NotFound();
        if (!requestContext.IsPlatformAdmin && message.BusinessId != requestContext.BusinessId) return Forbid();
        if (!message.DeadLetteredAtUtc.HasValue)
            return Conflict(new ProblemDetails { Title = "Mesaj dead-letter durumunda değil.", Status = StatusCodes.Status409Conflict });
        int previousAttempts = message.Attempts;
        message.Retry(timeProvider.GetUtcNow());
        context.AuditLogs.Add(AuditLog.Create(requestContext.UserId, message.BusinessId,
            "Retry", nameof(NotificationOutboxMessage), message.Id.ToString(),
            JsonSerializer.Serialize(new { previousAttempts, reason = "manual_retry" })));
        await context.SaveChangesAsync(cancellationToken);
        return Accepted(new { message.Id, message.NextAttemptAtUtc });
    }
}

public sealed record NotificationOutboxItemResponse(Guid Id, Guid BusinessId, Guid OrderId, string EventType,
    int Attempts, int? LastHttpStatusCode, string? LastError, DateTimeOffset CreatedAtUtc,
    DateTimeOffset NextAttemptAtUtc, DateTimeOffset? ProcessedAtUtc, DateTimeOffset? DeadLetteredAtUtc);

using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.Core.Queries.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DeliveryOps.Core.Api.Controllers;

[ApiController]
[Route("api/v1/operations/integration-outbox")]
public sealed class IntegrationOutboxController(CoreDbContext context, IRequestContext requestContext,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.IntegrationsRead)]
    public async Task<ActionResult<PagedResponse<IntegrationOutboxItemResponse>>> Get(
        [FromQuery] bool deadLetteredOnly = true, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        IQueryable<IntegrationOutboxMessage> query = context.IntegrationOutbox.AsNoTracking();
        if (!requestContext.IsPlatformAdmin)
        {
            if (!requestContext.BusinessId.HasValue) return Forbid();
            query = query.Where(x => x.BusinessId == requestContext.BusinessId.Value);
        }
        if (deadLetteredOnly) query = query.Where(x => x.DeadLetteredAtUtc != null);
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        int totalCount = await query.CountAsync(cancellationToken);
        List<IntegrationOutboxItemResponse> items = await query
            .OrderByDescending(x => x.DeadLetteredAtUtc ?? x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new IntegrationOutboxItemResponse(x.Id, x.BusinessId, x.OrderId, x.EventType,
                x.Attempts, x.LastHttpStatusCode, x.LastError, x.CreatedAtUtc, x.NextAttemptAtUtc,
                x.ProcessedAtUtc, x.DeadLetteredAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(new PagedResponse<IntegrationOutboxItemResponse>(items, page, pageSize, totalCount));
    }

    [HttpPost("{id:guid}/retry")]
    [Authorize(Policy = Permissions.IntegrationsWrite)]
    public async Task<ActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        IntegrationOutboxMessage? message = await context.IntegrationOutbox.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (message is null) return NotFound();
        if (!requestContext.IsPlatformAdmin && message.BusinessId != requestContext.BusinessId) return Forbid();
        if (!message.DeadLetteredAtUtc.HasValue)
            return Conflict(new ProblemDetails { Title = "Mesaj dead-letter durumunda değil.", Status = StatusCodes.Status409Conflict });
        int previousAttempts = message.Attempts;
        message.Retry(timeProvider.GetUtcNow());
        context.AuditLogs.Add(AuditLog.Create(requestContext.UserId, message.BusinessId,
            "Retry", nameof(IntegrationOutboxMessage), message.Id.ToString(),
            JsonSerializer.Serialize(new { previousAttempts, reason = "manual_retry" })));
        await context.SaveChangesAsync(cancellationToken);
        return Accepted(new { message.Id, message.NextAttemptAtUtc });
    }
}

public sealed record IntegrationOutboxItemResponse(Guid Id, Guid BusinessId, Guid OrderId, string EventType,
    int Attempts, int? LastHttpStatusCode, string? LastError, DateTimeOffset CreatedAtUtc,
    DateTimeOffset NextAttemptAtUtc, DateTimeOffset? ProcessedAtUtc, DateTimeOffset? DeadLetteredAtUtc);

using DeliveryOps.BuildingBlocks.Application;
using MediatR;

namespace DeliveryOps.Core.Queries.Audit;

public sealed record AuditLogResponse(Guid Id, Guid UserId, Guid? BusinessId, string Action, string EntityName, string EntityId, string ChangesJson, DateTimeOffset CreatedAtUtc);
public sealed record GetAuditLogsQuery(Guid? BusinessId, int Page = 1, int PageSize = 50, string? EntityName = null)
    : IRequest<Result<PagedResponse<AuditLogResponse>>>;

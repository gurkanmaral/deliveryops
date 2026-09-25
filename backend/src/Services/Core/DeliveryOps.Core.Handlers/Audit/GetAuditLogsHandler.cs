using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Audit;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Audit;

public sealed class GetAuditLogsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetAuditLogsQuery, Result<PagedResponse<AuditLogResponse>>>
{
    public async Task<Result<PagedResponse<AuditLogResponse>>> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        IQueryable<AuditLog> query = context.AuditLogs.AsNoTracking();
        if (businessId.HasValue) query = query.Where(x => x.BusinessId == businessId.Value);
        if (!string.IsNullOrWhiteSpace(request.EntityName)) query = query.Where(x => x.EntityName == request.EntityName);
        PagedResponse<AuditLogResponse> response = await query.OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new AuditLogResponse(x.Id, x.UserId, x.BusinessId, x.Action, x.EntityName, x.EntityId, x.ChangesJson, x.CreatedAtUtc))
            .ToPagedAsync(request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<AuditLogResponse>>.Success(response);
    }
}

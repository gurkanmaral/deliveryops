using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Operations;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Operations;

public sealed class GetSlaSettingsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetSlaSettingsQuery, Result<SlaSettingsResponse>>
{
    public async Task<Result<SlaSettingsResponse>> Handle(GetSlaSettingsQuery request, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!businessId.HasValue) return Result<SlaSettingsResponse>.Failure(HandlerErrors.Validation("İşletme seçilmelidir."));
        if (!TenantAccess.CanAccess(requestContext, businessId.Value)) return Result<SlaSettingsResponse>.Failure(HandlerErrors.Forbidden);
        if (!await context.Businesses.AnyAsync(x => x.Id == businessId.Value && x.IsActive, cancellationToken))
            return Result<SlaSettingsResponse>.Failure(HandlerErrors.NotFound("İşletme"));
        BusinessSlaSettings? settings = await context.BusinessSlaSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == businessId.Value, cancellationToken);
        return Result<SlaSettingsResponse>.Success(Map(settings ?? BusinessSlaSettings.CreateDefault(businessId.Value)));
    }

    public static SlaSettingsResponse Map(BusinessSlaSettings settings) => new(settings.BusinessId,
        settings.CourierWaitingWarningMinutes, settings.CourierWaitingCriticalMinutes,
        settings.PickupWarningMinutes, settings.PickupCriticalMinutes,
        settings.DeliveryWarningMinutes, settings.DeliveryCriticalMinutes,
        settings.LocationStaleWarningMinutes, settings.LocationStaleCriticalMinutes);
}

public sealed class UpdateSlaSettingsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<UpdateSlaSettingsCommand, Result<SlaSettingsResponse>>
{
    public async Task<Result<SlaSettingsResponse>> Handle(UpdateSlaSettingsCommand request, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!businessId.HasValue) return Result<SlaSettingsResponse>.Failure(HandlerErrors.Validation("İşletme seçilmelidir."));
        if (!TenantAccess.CanAccess(requestContext, businessId.Value)) return Result<SlaSettingsResponse>.Failure(HandlerErrors.Forbidden);
        if (!await context.Businesses.AnyAsync(x => x.Id == businessId.Value && x.IsActive, cancellationToken))
            return Result<SlaSettingsResponse>.Failure(HandlerErrors.NotFound("İşletme"));
        BusinessSlaSettings? settings = await context.BusinessSlaSettings
            .SingleOrDefaultAsync(x => x.BusinessId == businessId.Value, cancellationToken);
        if (settings is null)
        {
            settings = BusinessSlaSettings.CreateDefault(businessId.Value);
            context.BusinessSlaSettings.Add(settings);
        }
        try
        {
            settings.Update(request.CourierWaitingWarningMinutes, request.CourierWaitingCriticalMinutes,
                request.PickupWarningMinutes, request.PickupCriticalMinutes,
                request.DeliveryWarningMinutes, request.DeliveryCriticalMinutes,
                request.LocationStaleWarningMinutes, request.LocationStaleCriticalMinutes);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Result<SlaSettingsResponse>.Failure(HandlerErrors.Validation(exception.Message));
        }
        await context.SaveChangesAsync(cancellationToken);
        return Result<SlaSettingsResponse>.Success(GetSlaSettingsHandler.Map(settings));
    }
}

public sealed class GetOperationalAlertsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetOperationalAlertsQuery, Result<PagedResponse<OperationalAlertResponse>>>
{
    public async Task<Result<PagedResponse<OperationalAlertResponse>>> Handle(GetOperationalAlertsQuery request, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!requestContext.IsPlatformAdmin && !businessId.HasValue)
            return Result<PagedResponse<OperationalAlertResponse>>.Failure(HandlerErrors.Forbidden);
        IQueryable<OperationalAlert> query = context.OperationalAlerts.AsNoTracking();
        if (businessId.HasValue) query = query.Where(x => x.BusinessId == businessId.Value);
        if (!request.IncludeResolved) query = query.Where(x => x.Status != OperationalAlertStatus.Resolved);
        if (request.MinimumSeverity.HasValue) query = query.Where(x => x.Severity >= request.MinimumSeverity.Value);
        PagedResponse<OperationalAlertResponse> alerts = await query
            .OrderBy(x => x.Status == OperationalAlertStatus.Resolved)
            .ThenByDescending(x => x.Severity).ThenByDescending(x => x.LastChangedAtUtc)
            .Select(x => new OperationalAlertResponse(x.Id, x.BusinessId, x.OrderId, x.CourierId,
                x.Type, x.Severity, x.Status, x.Title, x.Message, x.FirstDetectedAtUtc, x.LastChangedAtUtc,
                x.AcknowledgedAtUtc, x.ResolvedAtUtc))
            .ToPagedAsync(request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<OperationalAlertResponse>>.Success(alerts);
    }
}

public sealed class AcknowledgeOperationalAlertHandler(ICoreDbContext context, IRequestContext requestContext,
    IOperationalAlertNotifier notifier)
    : IRequestHandler<AcknowledgeOperationalAlertCommand, Result<OperationalAlertResponse>>
{
    public async Task<Result<OperationalAlertResponse>> Handle(AcknowledgeOperationalAlertCommand request, CancellationToken cancellationToken)
    {
        OperationalAlert? alert = await context.OperationalAlerts.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (alert is null) return Result<OperationalAlertResponse>.Failure(HandlerErrors.NotFound("Uyarı"));
        if (!TenantAccess.CanAccess(requestContext, alert.BusinessId)) return Result<OperationalAlertResponse>.Failure(HandlerErrors.Forbidden);
        try { alert.Acknowledge(requestContext.UserId, DateTimeOffset.UtcNow); }
        catch (InvalidOperationException exception) { return Result<OperationalAlertResponse>.Failure(HandlerErrors.Conflict(exception.Message)); }
        await context.SaveChangesAsync(cancellationToken);
        OperationalAlertResponse response = OperationalAlertMapper.Map(alert);
        await notifier.AlertChangedAsync(response, cancellationToken);
        return Result<OperationalAlertResponse>.Success(response);
    }
}

public static class OperationalAlertMapper
{
    public static OperationalAlertResponse Map(OperationalAlert x) => new(x.Id, x.BusinessId, x.OrderId,
        x.CourierId, x.Type, x.Severity, x.Status, x.Title, x.Message, x.FirstDetectedAtUtc,
        x.LastChangedAtUtc, x.AcknowledgedAtUtc, x.ResolvedAtUtc);
}

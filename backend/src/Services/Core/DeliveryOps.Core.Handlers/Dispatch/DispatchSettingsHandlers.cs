using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Dispatch;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Dispatch;

public sealed class GetDispatchSettingsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetDispatchSettingsQuery, Result<DispatchSettingsResponse>>
{
    public async Task<Result<DispatchSettingsResponse>> Handle(GetDispatchSettingsQuery request, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!businessId.HasValue) return Result<DispatchSettingsResponse>.Failure(HandlerErrors.Validation("İşletme seçilmelidir."));
        if (!TenantAccess.CanAccess(requestContext, businessId.Value)) return Result<DispatchSettingsResponse>.Failure(HandlerErrors.Forbidden);
        if (!await context.Businesses.AnyAsync(x => x.Id == businessId.Value && x.IsActive, cancellationToken))
            return Result<DispatchSettingsResponse>.Failure(HandlerErrors.NotFound("İşletme"));

        BusinessDispatchSettings? settings = await context.BusinessDispatchSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == businessId.Value, cancellationToken);
        return Result<DispatchSettingsResponse>.Success(Map(settings, businessId.Value));
    }

    internal static DispatchSettingsResponse Map(BusinessDispatchSettings? settings, Guid businessId) => settings is null
        ? new DispatchSettingsResponse(businessId, false, false, true, true, 2, true, 5, 10, true, 2, 45)
        : new DispatchSettingsResponse(settings.BusinessId, settings.AutoConfirmOrders, settings.AutoAssignCouriers,
            settings.AllowCourierSelfClaim, settings.PreferBranchCouriers, settings.MaxActiveOrdersPerCourier,
            settings.RequireFreshLocation, settings.LocationFreshnessMinutes, settings.AssignmentRadiusKm,
            settings.PreferDeliveryClusters, settings.DeliveryClusterRadiusKm,
            settings.DeliveryClusterMaxBearingDegrees);
}

public sealed class UpdateDispatchSettingsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<UpdateDispatchSettingsCommand, Result<DispatchSettingsResponse>>
{
    public async Task<Result<DispatchSettingsResponse>> Handle(UpdateDispatchSettingsCommand request, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!businessId.HasValue) return Result<DispatchSettingsResponse>.Failure(HandlerErrors.Validation("İşletme seçilmelidir."));
        if (!TenantAccess.CanAccess(requestContext, businessId.Value)) return Result<DispatchSettingsResponse>.Failure(HandlerErrors.Forbidden);
        if (!await context.Businesses.AnyAsync(x => x.Id == businessId.Value && x.IsActive, cancellationToken))
            return Result<DispatchSettingsResponse>.Failure(HandlerErrors.NotFound("İşletme"));

        BusinessDispatchSettings? settings = await context.BusinessDispatchSettings
            .SingleOrDefaultAsync(x => x.BusinessId == businessId.Value, cancellationToken);
        if (settings is null)
        {
            settings = BusinessDispatchSettings.CreateDefault(businessId.Value);
            context.BusinessDispatchSettings.Add(settings);
        }
        bool wasAutoAssignEnabled = settings.AutoAssignCouriers;

        try
        {
            settings.Update(request.AutoConfirmOrders, request.AutoAssignCouriers, request.AllowCourierSelfClaim,
                request.PreferBranchCouriers, request.MaxActiveOrdersPerCourier, request.RequireFreshLocation,
                request.LocationFreshnessMinutes, request.AssignmentRadiusKm, request.PreferDeliveryClusters,
                request.DeliveryClusterRadiusKm, request.DeliveryClusterMaxBearingDegrees);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Result<DispatchSettingsResponse>.Failure(HandlerErrors.Validation(exception.Message));
        }

        if (wasAutoAssignEnabled != settings.AutoAssignCouriers)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<OrderDispatchState> waitingStates = await context.OrderDispatchStates
                .Where(state => state.BusinessId == businessId.Value &&
                    context.Orders.Any(order => order.Id == state.OrderId && order.Status == OrderStatus.WaitingForCourier))
                .ToListAsync(cancellationToken);
            foreach (OrderDispatchState state in waitingStates)
            {
                if (settings.AutoAssignCouriers) state.Queue(now);
                else state.MarkDisabled(now);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result<DispatchSettingsResponse>.Success(GetDispatchSettingsHandler.Map(settings, businessId.Value));
    }
}

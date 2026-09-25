using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Shifts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Shifts;

public sealed class StartCourierShiftHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<StartCourierShiftCommand, Result<CourierShiftResponse>>
{
    public async Task<Result<CourierShiftResponse>> Handle(StartCourierShiftCommand request, CancellationToken cancellationToken)
    {
        Courier? courier = await context.Couriers.FindAsync([request.CourierId], cancellationToken);
        if (courier is null || !courier.IsActive) return Result<CourierShiftResponse>.Failure(HandlerErrors.NotFound("Kurye"));
        if (requestContext.CourierId != courier.Id && !TenantAccess.CanAccess(requestContext, courier.BusinessId))
            return Result<CourierShiftResponse>.Failure(HandlerErrors.Forbidden);
        if (await context.CourierShifts.AnyAsync(x => x.CourierId == courier.Id && x.EndedAtUtc == null, cancellationToken))
            return Result<CourierShiftResponse>.Failure(HandlerErrors.Conflict("Kurye zaten mesaide."));
        CourierShift shift = CourierShift.Start(courier.Id, courier.BusinessId, DateTimeOffset.UtcNow);
        context.CourierShifts.Add(shift);
        courier.SetAvailability(CourierAvailability.Available);
        await context.SaveChangesAsync(cancellationToken);
        return Result<CourierShiftResponse>.Success(Map(shift));
    }
    private static CourierShiftResponse Map(CourierShift x) => new(x.Id, x.CourierId, x.BusinessId, x.StartedAtUtc, x.EndedAtUtc);
}

public sealed class EndCourierShiftHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<EndCourierShiftCommand, Result<CourierShiftResponse>>
{
    public async Task<Result<CourierShiftResponse>> Handle(EndCourierShiftCommand request, CancellationToken cancellationToken)
    {
        Courier? courier = await context.Couriers.FindAsync([request.CourierId], cancellationToken);
        if (courier is null) return Result<CourierShiftResponse>.Failure(HandlerErrors.NotFound("Kurye"));
        if (requestContext.CourierId != courier.Id && !TenantAccess.CanAccess(requestContext, courier.BusinessId))
            return Result<CourierShiftResponse>.Failure(HandlerErrors.Forbidden);
        CourierShift? shift = await context.CourierShifts.SingleOrDefaultAsync(x => x.CourierId == courier.Id && x.EndedAtUtc == null, cancellationToken);
        if (shift is null) return Result<CourierShiftResponse>.Failure(HandlerErrors.NotFound("Aktif mesai"));
        shift.End(DateTimeOffset.UtcNow);
        courier.SetAvailability(CourierAvailability.OffShift);
        await context.SaveChangesAsync(cancellationToken);
        return Result<CourierShiftResponse>.Success(new(shift.Id, shift.CourierId, shift.BusinessId, shift.StartedAtUtc, shift.EndedAtUtc));
    }
}

public sealed class GetActiveCourierShiftsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetActiveCourierShiftsQuery, Result<PagedResponse<CourierShiftResponse>>>
{
    public async Task<Result<PagedResponse<CourierShiftResponse>>> Handle(GetActiveCourierShiftsQuery request, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!requestContext.IsPlatformAdmin && businessId is null) return Result<PagedResponse<CourierShiftResponse>>.Failure(HandlerErrors.Forbidden);
        IQueryable<CourierShift> query = context.CourierShifts.AsNoTracking().Where(x => x.EndedAtUtc == null);
        if (businessId.HasValue) query = query.Where(x => x.BusinessId == businessId.Value);
        PagedResponse<CourierShiftResponse> result = await query.OrderByDescending(x => x.StartedAtUtc)
            .Select(x => new CourierShiftResponse(x.Id, x.CourierId, x.BusinessId, x.StartedAtUtc, x.EndedAtUtc))
            .ToPagedAsync(request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<CourierShiftResponse>>.Success(result);
    }
}

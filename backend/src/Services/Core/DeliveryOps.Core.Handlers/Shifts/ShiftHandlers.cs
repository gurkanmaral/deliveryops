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
        if (!TenantAccess.CanManageCourier(requestContext, courier.Id, courier.BusinessId, courier.BranchId))
            return Result<CourierShiftResponse>.Failure(HandlerErrors.Forbidden);
        if (await context.CourierShifts.AnyAsync(x => x.CourierId == courier.Id && x.EndedAtUtc == null, cancellationToken))
            return Result<CourierShiftResponse>.Failure(HandlerErrors.Conflict("Kurye zaten mesaide."));
        CourierShift shift = CourierShift.Start(courier.Id, courier.BusinessId, DateTimeOffset.UtcNow);
        context.CourierShifts.Add(shift);
        courier.SetAvailability(CourierAvailability.Available);
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CourierShiftResponse>.Failure(HandlerErrors.Conflict("Kurye kaydı başka bir işlem tarafından güncellendi. Tekrar deneyin."));
        }
        catch (DbUpdateException)
        {
            // The unique open-shift index rejects a concurrent second start (e.g. a double tap).
            return Result<CourierShiftResponse>.Failure(HandlerErrors.Conflict("Kurye zaten mesaide."));
        }
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
        if (!TenantAccess.CanManageCourier(requestContext, courier.Id, courier.BusinessId, courier.BranchId))
            return Result<CourierShiftResponse>.Failure(HandlerErrors.Forbidden);
        CourierShift? shift = await context.CourierShifts.SingleOrDefaultAsync(x => x.CourierId == courier.Id && x.EndedAtUtc == null, cancellationToken);
        if (shift is null) return Result<CourierShiftResponse>.Failure(HandlerErrors.NotFound("Aktif mesai"));
        // Ending a shift while holding packages would leave them with an off-shift courier that dispatch ignores.
        if (await CourierWorkload.HasActiveOrdersAsync(context, courier.Id, cancellationToken))
            return Result<CourierShiftResponse>.Failure(HandlerErrors.Conflict(
                "Aktif paketler teslim edilmeden veya başka kuryeye aktarılmadan mesai bitirilemez."));
        shift.End(DateTimeOffset.UtcNow);
        courier.SetAvailability(CourierAvailability.OffShift);
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CourierShiftResponse>.Failure(HandlerErrors.Conflict("Kurye kaydı başka bir işlem tarafından güncellendi. Tekrar deneyin."));
        }
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
        if (requestContext.CourierId.HasValue)
        {
            Guid courierId = requestContext.CourierId.Value;
            query = query.Where(x => x.CourierId == courierId);
        }
        if (requestContext.BranchId.HasValue)
            query = query.Where(x => context.Couriers.Any(courier =>
                courier.Id == x.CourierId && courier.BranchId == requestContext.BranchId.Value));
        PagedResponse<CourierShiftResponse> result = await query.OrderByDescending(x => x.StartedAtUtc)
            .Select(x => new CourierShiftResponse(x.Id, x.CourierId, x.BusinessId, x.StartedAtUtc, x.EndedAtUtc))
            .ToPagedAsync(request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<CourierShiftResponse>>.Success(result);
    }
}

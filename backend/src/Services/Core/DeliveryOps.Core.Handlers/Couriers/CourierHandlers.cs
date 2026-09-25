using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Couriers;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Couriers;

public sealed class GetCouriersHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetCouriersQuery, Result<PagedResponse<CourierResponse>>>
{
    public async Task<Result<PagedResponse<CourierResponse>>> Handle(GetCouriersQuery request, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!requestContext.IsPlatformAdmin && businessId is null) return Result<PagedResponse<CourierResponse>>.Failure(HandlerErrors.Forbidden);
        IQueryable<Courier> query = context.Couriers.AsNoTracking();
        if (businessId.HasValue) query = query.Where(x => x.BusinessId == businessId.Value);
        Guid? branchId = requestContext.BranchId ?? request.BranchId;
        if (branchId.HasValue) query = query.Where(x => x.BranchId == branchId.Value);
        if (!request.IncludeInactive) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(request.Search)) query = query.Where(x => x.FirstName.Contains(request.Search) || x.LastName.Contains(request.Search) || x.PhoneNumber.Contains(request.Search));
        query = request.Sort.ToLowerInvariant() switch { "-name" => query.OrderByDescending(x => x.FirstName).ThenByDescending(x => x.LastName), "-created" => query.OrderByDescending(x => x.CreatedAtUtc), _ => query.OrderBy(x => x.FirstName).ThenBy(x => x.LastName) };
        PagedResponse<CourierResponse> response = await query.Select(x => new CourierResponse(x.Id, x.BusinessId, x.BranchId, x.FirstName, x.LastName, x.PhoneNumber, x.Availability, x.DeliveryStatus, x.IsActive))
            .ToPagedAsync(request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<CourierResponse>>.Success(response);
    }
}

public sealed class GetCourierHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetCourierQuery, Result<CourierResponse>>
{
    public async Task<Result<CourierResponse>> Handle(GetCourierQuery request, CancellationToken cancellationToken)
    {
        Courier? courier = await context.Couriers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (courier is null) return Result<CourierResponse>.Failure(HandlerErrors.NotFound("Kurye"));
        if (!TenantAccess.CanAccess(requestContext, courier.BusinessId)) return Result<CourierResponse>.Failure(HandlerErrors.Forbidden);
        return Result<CourierResponse>.Success(CourierMapper.Map(courier));
    }
}

public sealed class GetCurrentCourierHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetCurrentCourierQuery, Result<CurrentCourierResponse>>
{
    public async Task<Result<CurrentCourierResponse>> Handle(GetCurrentCourierQuery request, CancellationToken cancellationToken)
    {
        if (!requestContext.CourierId.HasValue) return Result<CurrentCourierResponse>.Failure(HandlerErrors.Forbidden);
        Courier? courier = await context.Couriers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestContext.CourierId.Value, cancellationToken);
        if (courier is null) return Result<CurrentCourierResponse>.Failure(HandlerErrors.NotFound("Kurye"));
        CourierShift? shift = await context.CourierShifts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CourierId == courier.Id && x.EndedAtUtc == null, cancellationToken);
        return Result<CurrentCourierResponse>.Success(new CurrentCourierResponse(courier.Id, courier.BusinessId,
            courier.BranchId, courier.FirstName, courier.LastName, courier.PhoneNumber, courier.Availability,
            courier.DeliveryStatus, courier.IsActive, shift is not null, shift?.StartedAtUtc));
    }
}

public sealed class CreateCourierHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<CreateCourierCommand, Result<CourierResponse>>
{
    public async Task<Result<CourierResponse>> Handle(CreateCourierCommand request, CancellationToken cancellationToken)
    {
        if (!TenantAccess.CanAccess(requestContext, request.BusinessId)) return Result<CourierResponse>.Failure(HandlerErrors.Forbidden);
        if (request.BranchId.HasValue && !await context.Branches.AnyAsync(x => x.Id == request.BranchId && x.BusinessId == request.BusinessId && x.IsActive, cancellationToken))
            return Result<CourierResponse>.Failure(HandlerErrors.NotFound("Şube"));
        if (await context.Couriers.AnyAsync(x => x.PhoneNumber == request.PhoneNumber.Trim(), cancellationToken))
            return Result<CourierResponse>.Failure(HandlerErrors.Conflict("Bu telefon numarası zaten kullanılıyor."));

        Courier courier = Courier.Create(request.BusinessId, request.BranchId, request.FirstName, request.LastName, request.PhoneNumber);
        context.Couriers.Add(courier);
        await context.SaveChangesAsync(cancellationToken);
        return Result<CourierResponse>.Success(CourierMapper.Map(courier));
    }
}

public sealed class UpdateCourierHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<UpdateCourierCommand, Result<CourierResponse>>
{
    public async Task<Result<CourierResponse>> Handle(UpdateCourierCommand request, CancellationToken cancellationToken)
    {
        Courier? courier = await context.Couriers.FindAsync([request.Id], cancellationToken);
        if (courier is null) return Result<CourierResponse>.Failure(HandlerErrors.NotFound("Kurye"));
        if (!TenantAccess.CanAccess(requestContext, courier.BusinessId)) return Result<CourierResponse>.Failure(HandlerErrors.Forbidden);
        if (await context.Couriers.AnyAsync(x => x.Id != request.Id && x.PhoneNumber == request.PhoneNumber.Trim(), cancellationToken))
            return Result<CourierResponse>.Failure(HandlerErrors.Conflict("Bu telefon numarası zaten kullanılıyor."));
        courier.Update(request.FirstName, request.LastName, request.PhoneNumber);
        await context.SaveChangesAsync(cancellationToken);
        return Result<CourierResponse>.Success(CourierMapper.Map(courier));
    }
}

public sealed class AssignCourierToBusinessHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<AssignCourierToBusinessCommand, Result<CourierResponse>>
{
    public async Task<Result<CourierResponse>> Handle(AssignCourierToBusinessCommand request, CancellationToken cancellationToken)
    {
        if (!requestContext.IsPlatformAdmin) return Result<CourierResponse>.Failure(HandlerErrors.Forbidden);
        Courier? courier = await context.Couriers.FindAsync([request.Id], cancellationToken);
        if (courier is null) return Result<CourierResponse>.Failure(HandlerErrors.NotFound("Kurye"));
        if (!await context.Businesses.AnyAsync(x => x.Id == request.BusinessId && x.IsActive, cancellationToken))
            return Result<CourierResponse>.Failure(HandlerErrors.NotFound("İşletme"));
        if (request.BranchId.HasValue && !await context.Branches.AnyAsync(x => x.Id == request.BranchId && x.BusinessId == request.BusinessId, cancellationToken))
            return Result<CourierResponse>.Failure(HandlerErrors.NotFound("Şube"));
        courier.AssignTo(request.BusinessId, request.BranchId);
        await context.SaveChangesAsync(cancellationToken);
        return Result<CourierResponse>.Success(CourierMapper.Map(courier));
    }
}

public sealed class UpdateCourierStatusHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<UpdateCourierStatusCommand, Result<CourierResponse>>
{
    public async Task<Result<CourierResponse>> Handle(UpdateCourierStatusCommand request, CancellationToken cancellationToken)
    {
        Courier? courier = await context.Couriers.FindAsync([request.Id], cancellationToken);
        if (courier is null) return Result<CourierResponse>.Failure(HandlerErrors.NotFound("Kurye"));
        if (!TenantAccess.CanAccess(requestContext, courier.BusinessId)) return Result<CourierResponse>.Failure(HandlerErrors.Forbidden);
        courier.SetAvailability(request.Availability);
        courier.SetDeliveryStatus(request.DeliveryStatus);
        await context.SaveChangesAsync(cancellationToken);
        return Result<CourierResponse>.Success(CourierMapper.Map(courier));
    }
}

public sealed class DeactivateCourierHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<DeactivateCourierCommand, Result>
{
    public async Task<Result> Handle(DeactivateCourierCommand request, CancellationToken cancellationToken)
    {
        Courier? courier = await context.Couriers.FindAsync([request.Id], cancellationToken);
        if (courier is null) return Result.Failure(HandlerErrors.NotFound("Kurye"));
        if (!TenantAccess.CanAccess(requestContext, courier.BusinessId)) return Result.Failure(HandlerErrors.Forbidden);
        courier.Deactivate();
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class CourierMapper
{
    public static CourierResponse Map(Courier x) => new(x.Id, x.BusinessId, x.BranchId, x.FirstName, x.LastName, x.PhoneNumber, x.Availability, x.DeliveryStatus, x.IsActive);
}

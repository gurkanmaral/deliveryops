using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Locations;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Locations;

public sealed class RecordCourierLocationHandler(ICoreDbContext context, IRequestContext requestContext,
    ICourierPresenceStore presenceStore, IOperationsNotifier notifier)
    : IRequestHandler<RecordCourierLocationCommand, Result<CourierLocationSnapshot>>
{
    public async Task<Result<CourierLocationSnapshot>> Handle(RecordCourierLocationCommand request, CancellationToken cancellationToken)
    {
        Courier? courier = await context.Couriers.FindAsync([request.CourierId], cancellationToken);
        if (courier is null || !courier.IsActive) return Result<CourierLocationSnapshot>.Failure(HandlerErrors.NotFound("Kurye"));
        bool isSelf = requestContext.CourierId == courier.Id;
        if (!isSelf && !TenantAccess.CanAccessBranch(requestContext, courier.BusinessId, courier.BranchId))
            return Result<CourierLocationSnapshot>.Failure(HandlerErrors.Forbidden);

        DateTimeOffset recordedAt = request.RecordedAtUtc ?? DateTimeOffset.UtcNow;
        if (recordedAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return Result<CourierLocationSnapshot>.Failure(new Error("validation", "Konum zamanı gelecekte olamaz."));
        if (recordedAt < DateTimeOffset.UtcNow.AddHours(-2))
            return Result<CourierLocationSnapshot>.Failure(new Error("validation", "İki saatten eski konum kaydı kabul edilemez."));
        if (isSelf && !await context.CourierShifts.AnyAsync(x => x.CourierId == courier.Id && x.EndedAtUtc == null, cancellationToken))
            return Result<CourierLocationSnapshot>.Failure(HandlerErrors.Conflict("Konum paylaşmak için mesaiyi başlatın."));

        CourierLocation location = CourierLocation.Create(courier.Id, courier.BusinessId, request.Latitude,
            request.Longitude, request.AccuracyMeters, request.SpeedMetersPerSecond, request.HeadingDegrees, recordedAt);
        context.CourierLocations.Add(location);
        courier.RecordLocation(recordedAt);
        await context.SaveChangesAsync(cancellationToken);

        CourierLocationSnapshot snapshot = Map(courier, location, false);
        await presenceStore.SetAsync(snapshot, cancellationToken);
        await notifier.LocationUpdatedAsync(snapshot, cancellationToken);
        return Result<CourierLocationSnapshot>.Success(snapshot);
    }

    private static CourierLocationSnapshot Map(Courier courier, CourierLocation location, bool isStale) =>
        new(courier.Id, courier.BusinessId, $"{courier.FirstName} {courier.LastName}", location.Position.Y,
            location.Position.X, location.AccuracyMeters, location.SpeedMetersPerSecond, location.HeadingDegrees,
            location.RecordedAtUtc, isStale, courier.Availability, courier.DeliveryStatus);
}

public sealed class GetLatestCourierLocationsHandler(ICoreDbContext context, IRequestContext requestContext,
    ICourierPresenceStore presenceStore)
    : IRequestHandler<GetLatestCourierLocationsQuery, Result<PagedResponse<CourierLocationSnapshot>>>
{
    public async Task<Result<PagedResponse<CourierLocationSnapshot>>> Handle(GetLatestCourierLocationsQuery request, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!requestContext.IsPlatformAdmin && businessId is null)
            return Result<PagedResponse<CourierLocationSnapshot>>.Failure(HandlerErrors.Forbidden);

        IQueryable<Courier> courierQuery = context.Couriers.AsNoTracking().Where(x => x.IsActive);
        if (businessId.HasValue) courierQuery = courierQuery.Where(x => x.BusinessId == businessId.Value);
        if (requestContext.BranchId.HasValue)
            courierQuery = courierQuery.Where(x => x.BranchId == requestContext.BranchId.Value);
        List<Courier> couriers = await courierQuery.ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, CourierLocationSnapshot> cached = await presenceStore.GetAsync(couriers.Select(x => x.Id), cancellationToken);
        Guid[] missingIds = couriers.Where(x => !cached.ContainsKey(x.Id)).Select(x => x.Id).ToArray();
        List<CourierLocation> latestFromDatabase = [];
        if (missingIds.Length > 0)
        {
            List<CourierLocation> locations = await context.CourierLocations.AsNoTracking()
                .Where(x => missingIds.Contains(x.CourierId)).OrderByDescending(x => x.RecordedAtUtc)
                .ToListAsync(cancellationToken);
            latestFromDatabase = locations.GroupBy(x => x.CourierId).Select(x => x.First()).ToList();
        }

        DateTimeOffset staleBefore = DateTimeOffset.UtcNow.AddMinutes(-2);
        List<CourierLocationSnapshot> result = [];
        foreach (Courier courier in couriers)
        {
            if (cached.TryGetValue(courier.Id, out CourierLocationSnapshot? current))
            {
                result.Add(current with { IsStale = current.RecordedAtUtc < staleBefore, Availability = courier.Availability, DeliveryStatus = courier.DeliveryStatus });
                continue;
            }
            CourierLocation? location = latestFromDatabase.FirstOrDefault(x => x.CourierId == courier.Id);
            if (location is not null)
                result.Add(new CourierLocationSnapshot(courier.Id, courier.BusinessId, $"{courier.FirstName} {courier.LastName}",
                    location.Position.Y, location.Position.X, location.AccuracyMeters, location.SpeedMetersPerSecond,
                    location.HeadingDegrees, location.RecordedAtUtc, location.RecordedAtUtc < staleBefore,
                    courier.Availability, courier.DeliveryStatus));
        }
        return Result<PagedResponse<CourierLocationSnapshot>>.Success(Pagination.FromItems(
            result.OrderBy(x => x.CourierName).ToArray(), request.Page, request.PageSize));
    }
}

public sealed class GetCourierLocationHistoryHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetCourierLocationHistoryQuery, Result<PagedResponse<CourierLocationPoint>>>
{
    public async Task<Result<PagedResponse<CourierLocationPoint>>> Handle(GetCourierLocationHistoryQuery request, CancellationToken cancellationToken)
    {
        Courier? courier = await context.Couriers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.CourierId, cancellationToken);
        if (courier is null) return Result<PagedResponse<CourierLocationPoint>>.Failure(HandlerErrors.NotFound("Kurye"));
        if (!TenantAccess.CanAccessBranch(requestContext, courier.BusinessId, courier.BranchId) && requestContext.CourierId != courier.Id)
            return Result<PagedResponse<CourierLocationPoint>>.Failure(HandlerErrors.Forbidden);
        DateTimeOffset from = request.FromUtc ?? DateTimeOffset.UtcNow.AddHours(-8);
        DateTimeOffset to = request.ToUtc ?? DateTimeOffset.UtcNow;
        PagedResponse<CourierLocationPoint> points = await context.CourierLocations.AsNoTracking()
            .Where(x => x.CourierId == courier.Id && x.RecordedAtUtc >= from && x.RecordedAtUtc <= to)
            .OrderByDescending(x => x.RecordedAtUtc)
            .Select(x => new CourierLocationPoint(x.Position.Y, x.Position.X, x.AccuracyMeters,
                x.SpeedMetersPerSecond, x.HeadingDegrees, x.RecordedAtUtc))
            .ToPagedAsync(request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<CourierLocationPoint>>.Success(points with
        {
            Items = points.Items.Reverse().ToArray()
        });
    }
}

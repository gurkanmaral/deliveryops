using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Enums;
using MediatR;

namespace DeliveryOps.Core.Queries.Locations;

public sealed record CourierLocationSnapshot(Guid CourierId, Guid BusinessId, string CourierName,
    double Latitude, double Longitude, double? AccuracyMeters, double? SpeedMetersPerSecond,
    double? HeadingDegrees, DateTimeOffset RecordedAtUtc, bool IsStale,
    CourierAvailability Availability, DeliveryStatus DeliveryStatus);

public sealed record CourierLocationPoint(double Latitude, double Longitude, double? AccuracyMeters,
    double? SpeedMetersPerSecond, double? HeadingDegrees, DateTimeOffset RecordedAtUtc);

public sealed record RecordCourierLocationCommand(Guid CourierId, double Latitude, double Longitude,
    double? AccuracyMeters, double? SpeedMetersPerSecond, double? HeadingDegrees,
    DateTimeOffset? RecordedAtUtc) : IRequest<Result<CourierLocationSnapshot>>;

public sealed record GetLatestCourierLocationsQuery(Guid? BusinessId, int Page = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<CourierLocationSnapshot>>>;
public sealed record GetCourierLocationHistoryQuery(Guid CourierId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc,
    int Page = 1, int PageSize = 100) : IRequest<Result<PagedResponse<CourierLocationPoint>>>;

public interface ICourierPresenceStore
{
    Task SetAsync(CourierLocationSnapshot snapshot, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, CourierLocationSnapshot>> GetAsync(IEnumerable<Guid> courierIds, CancellationToken cancellationToken);
}

public interface IOperationsNotifier
{
    Task LocationUpdatedAsync(CourierLocationSnapshot snapshot, CancellationToken cancellationToken);
    Task LocationStaleAsync(Guid courierId, Guid businessId, CancellationToken cancellationToken);
}

using DeliveryOps.BuildingBlocks.Domain;
using NetTopologySuite.Geometries;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class CourierLocation : Entity
{
    private CourierLocation() { }
    public Guid CourierId { get; private init; }
    public Guid BusinessId { get; private init; }
    public Point Position { get; private init; } = null!;
    public double? AccuracyMeters { get; private init; }
    public double? SpeedMetersPerSecond { get; private init; }
    public double? HeadingDegrees { get; private init; }
    public DateTimeOffset RecordedAtUtc { get; private init; }

    public static CourierLocation Create(Guid courierId, Guid businessId, double latitude, double longitude,
        double? accuracy, double? speed, double? heading, DateTimeOffset recordedAtUtc) => new()
        {
            CourierId = courierId,
            BusinessId = businessId,
            Position = new Point(longitude, latitude) { SRID = 4326 },
            AccuracyMeters = accuracy,
            SpeedMetersPerSecond = speed,
            HeadingDegrees = heading,
            RecordedAtUtc = recordedAtUtc
        };
}

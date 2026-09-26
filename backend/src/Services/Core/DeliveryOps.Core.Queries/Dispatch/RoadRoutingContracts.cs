namespace DeliveryOps.Core.Queries.Dispatch;

public sealed record RoadRoutePoint(double Latitude, double Longitude);

public sealed record RoadRouteElement(
    int OriginIndex,
    int DestinationIndex,
    double DistanceKm,
    TimeSpan Duration);

public interface IRoadRouteDistanceProvider
{
    bool IsAvailable { get; }
    string ProviderName { get; }
    int MaxElementsPerRequest { get; }
    Task<IReadOnlyList<RoadRouteElement>> CalculateMatrixAsync(
        IReadOnlyList<RoadRoutePoint> origins,
        IReadOnlyList<RoadRoutePoint> destinations,
        CancellationToken cancellationToken);
}

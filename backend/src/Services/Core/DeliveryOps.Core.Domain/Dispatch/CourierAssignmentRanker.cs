namespace DeliveryOps.Core.Domain.Dispatch;

public sealed record CourierAssignmentCandidate(
    Guid CourierId,
    Guid? BranchId,
    int ActiveOrderCount,
    double? Latitude,
    double? Longitude,
    DateTimeOffset? LocationRecordedAtUtc);

public sealed record CourierAssignmentCriteria(
    Guid OrderBranchId,
    double? PickupLatitude,
    double? PickupLongitude,
    bool PreferBranchCouriers,
    int MaxActiveOrdersPerCourier,
    bool RequireFreshLocation,
    int LocationFreshnessMinutes,
    double? AssignmentRadiusKm,
    DateTimeOffset Now);

public static class CourierAssignmentRanker
{
    public static CourierAssignmentCandidate? SelectBest(
        IEnumerable<CourierAssignmentCandidate> candidates,
        CourierAssignmentCriteria criteria)
    {
        return candidates
            .Where(candidate => candidate.ActiveOrderCount < criteria.MaxActiveOrdersPerCourier)
            .Select(candidate => new RankedCandidate(candidate, DistanceFromPickup(candidate, criteria)))
            .Where(candidate => IsLocationEligible(candidate, criteria))
            .OrderBy(candidate => criteria.PreferBranchCouriers && candidate.Value.BranchId != criteria.OrderBranchId ? 1 : 0)
            .ThenBy(candidate => candidate.Value.ActiveOrderCount)
            .ThenBy(candidate => candidate.DistanceKm ?? double.MaxValue)
            .ThenByDescending(candidate => candidate.Value.LocationRecordedAtUtc)
            .ThenBy(candidate => candidate.Value.CourierId)
            .Select(candidate => candidate.Value)
            .FirstOrDefault();
    }

    private static bool IsLocationEligible(RankedCandidate candidate, CourierAssignmentCriteria criteria)
    {
        bool fresh = candidate.Value.LocationRecordedAtUtc.HasValue &&
                     candidate.Value.LocationRecordedAtUtc >= criteria.Now.AddMinutes(-criteria.LocationFreshnessMinutes);
        if (criteria.RequireFreshLocation && !fresh) return false;
        return !criteria.AssignmentRadiusKm.HasValue ||
               candidate.DistanceKm.HasValue && candidate.DistanceKm <= criteria.AssignmentRadiusKm.Value;
    }

    private static double? DistanceFromPickup(CourierAssignmentCandidate candidate, CourierAssignmentCriteria criteria)
    {
        return CalculateDistanceKm(candidate.Latitude, candidate.Longitude,
            criteria.PickupLatitude, criteria.PickupLongitude);
    }

    public static double? CalculateDistanceKm(double? latitude, double? longitude,
        double? targetLatitude, double? targetLongitude)
    {
        if (!latitude.HasValue || !longitude.HasValue || !targetLatitude.HasValue || !targetLongitude.HasValue) return null;

        const double earthRadiusKm = 6371.0088;
        double latitudeDelta = ToRadians(latitude.Value - targetLatitude.Value);
        double longitudeDelta = ToRadians(longitude.Value - targetLongitude.Value);
        double startLatitude = ToRadians(targetLatitude.Value);
        double endLatitude = ToRadians(latitude.Value);
        double a = Math.Pow(Math.Sin(latitudeDelta / 2), 2) +
                   Math.Cos(startLatitude) * Math.Cos(endLatitude) * Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
    private sealed record RankedCandidate(CourierAssignmentCandidate Value, double? DistanceKm);
}

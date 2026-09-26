using DeliveryOps.Core.Domain.Dispatch;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Dispatch;

public sealed class DispatchCandidateFinder(ICoreDbContext context, TimeProvider timeProvider,
    IRoadRouteDistanceProvider roadRoutes)
{
    private static readonly OrderStatus[] ActiveOrderStatuses =
        [OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.OnTheWay, OrderStatus.DeliveryFailed];

    public async Task<IReadOnlyList<CourierSuggestionResponse>> FindAsync(Order order,
        BusinessDispatchSettings settings, CancellationToken cancellationToken)
    {
        Branch branch = await context.Branches.AsNoTracking().SingleAsync(x => x.Id == order.BranchId, cancellationToken);
        List<Courier> couriers = await context.Couriers.AsNoTracking()
            .Where(x => x.BusinessId == order.BusinessId && x.IsActive && x.Availability == CourierAvailability.Available)
            .Where(x => !x.BranchId.HasValue || x.BranchId == order.BranchId)
            .Where(x => context.CourierShifts.Any(shift => shift.CourierId == x.Id && shift.EndedAtUtc == null))
            .ToListAsync(cancellationToken);
        if (couriers.Count == 0) return [];

        Guid[] courierIds = couriers.Select(x => x.Id).ToArray();
        Dictionary<Guid, int> activeCounts = await context.Orders.AsNoTracking()
            .Where(x => x.Id != order.Id && x.CourierId.HasValue && courierIds.Contains(x.CourierId.Value) && ActiveOrderStatuses.Contains(x.Status))
            .GroupBy(x => x.CourierId!.Value)
            .Select(group => new { CourierId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.CourierId, x => x.Count, cancellationToken);

        Dictionary<Guid, (double? DistanceKm, double? BearingDifferenceDegrees)> clusterMetrics = courierIds
            .ToDictionary(x => x, _ => ((double?)null, (double?)null));
        if (settings.PreferDeliveryClusters && roadRoutes.IsAvailable && order.DeliveryLatitude.HasValue &&
            order.DeliveryLongitude.HasValue &&
            order.DeliveryLocationAccuracy == DeliveryLocationAccuracy.Exact && branch.Latitude.HasValue &&
            branch.Longitude.HasValue)
        {
            var activeDeliveries = await context.Orders.AsNoTracking()
                .Where(x => x.Id != order.Id && x.CourierId.HasValue && courierIds.Contains(x.CourierId.Value) &&
                            ActiveOrderStatuses.Contains(x.Status) && x.DeliveryLatitude.HasValue &&
                            x.DeliveryLongitude.HasValue && x.DeliveryLocationAccuracy == DeliveryLocationAccuracy.Exact)
                .Select(x => new { CourierId = x.CourierId!.Value, x.DeliveryLatitude, x.DeliveryLongitude })
                .ToListAsync(cancellationToken);
            List<PreliminaryRouteMatch> preliminaryMatches = activeDeliveries.Select(activeOrder =>
            {
                double? distanceKm = CourierAssignmentRanker.CalculateDistanceKm(activeOrder.DeliveryLatitude,
                    activeOrder.DeliveryLongitude, order.DeliveryLatitude, order.DeliveryLongitude);
                double? bearing = CourierAssignmentRanker.CalculateBearingDifferenceDegrees(branch.Latitude,
                    branch.Longitude, activeOrder.DeliveryLatitude, activeOrder.DeliveryLongitude,
                    order.DeliveryLatitude, order.DeliveryLongitude);
                return new PreliminaryRouteMatch(activeOrder.CourierId, activeOrder.DeliveryLatitude!.Value,
                    activeOrder.DeliveryLongitude!.Value, distanceKm, bearing);
            }).Where(match => match.AirDistanceKm.HasValue &&
                              match.AirDistanceKm <= settings.DeliveryClusterRadiusKm &&
                              match.BearingDifferenceDegrees.HasValue &&
                              match.BearingDifferenceDegrees <= settings.DeliveryClusterMaxBearingDegrees)
                .OrderBy(match => match.AirDistanceKm)
                .ThenBy(match => match.BearingDifferenceDegrees)
                .Take(roadRoutes.MaxElementsPerRequest)
                .ToList();

            if (preliminaryMatches.Count > 0)
            {
                RoadRoutePoint newDestination = new(order.DeliveryLatitude.Value, order.DeliveryLongitude.Value);
                RoadRoutePoint[] activeDestinations = preliminaryMatches
                    .Select(match => new RoadRoutePoint(match.Latitude, match.Longitude)).ToArray();
                Task<IReadOnlyList<RoadRouteElement>> forwardTask = roadRoutes.CalculateMatrixAsync(
                    activeDestinations, [newDestination], cancellationToken);
                Task<IReadOnlyList<RoadRouteElement>> reverseTask = roadRoutes.CalculateMatrixAsync(
                    [newDestination], activeDestinations, cancellationToken);
                await Task.WhenAll(forwardTask, reverseTask);
                Dictionary<int, double> forward = forwardTask.Result.ToDictionary(x => x.OriginIndex, x => x.DistanceKm);
                Dictionary<int, double> reverse = reverseTask.Result.ToDictionary(x => x.DestinationIndex, x => x.DistanceKm);

                var verifiedMatches = preliminaryMatches.Select((match, index) => new
                    {
                        Match = match,
                        RoadDistanceKm = ResolveShortestRoadDistance(forward.GetValueOrDefault(index, double.MaxValue),
                            reverse.GetValueOrDefault(index, double.MaxValue))
                    })
                    .Where(match => match.RoadDistanceKm <= settings.DeliveryClusterRadiusKm)
                    .GroupBy(match => match.Match.CourierId);
                foreach (var group in verifiedMatches)
                {
                    var bestMatch = group.OrderBy(match =>
                        match.RoadDistanceKm / settings.DeliveryClusterRadiusKm +
                        match.Match.BearingDifferenceDegrees!.Value /
                        settings.DeliveryClusterMaxBearingDegrees).First();
                    clusterMetrics[group.Key] = (bestMatch.RoadDistanceKm,
                        bestMatch.Match.BearingDifferenceDegrees);
                }
            }
        }

        var latestTimes = context.CourierLocations.AsNoTracking()
            .Where(x => courierIds.Contains(x.CourierId))
            .GroupBy(x => x.CourierId)
            .Select(group => new { CourierId = group.Key, RecordedAtUtc = group.Max(x => x.RecordedAtUtc) });
        List<CourierLocation> latestRows = await (from location in context.CourierLocations.AsNoTracking()
                                                  join latest in latestTimes
                                                      on new { location.CourierId, location.RecordedAtUtc }
                                                      equals new { latest.CourierId, latest.RecordedAtUtc }
                                                  select location).ToListAsync(cancellationToken);
        Dictionary<Guid, CourierLocation> latestLocations = latestRows.GroupBy(x => x.CourierId)
            .ToDictionary(group => group.Key, group => group.First());

        Dictionary<Guid, Courier> courierLookup = couriers.ToDictionary(x => x.Id);
        List<CourierAssignmentCandidate> remaining = couriers.Select(courier =>
        {
            latestLocations.TryGetValue(courier.Id, out CourierLocation? location);
            var clusterMetric = clusterMetrics.GetValueOrDefault(courier.Id);
            return new CourierAssignmentCandidate(courier.Id, courier.BranchId, activeCounts.GetValueOrDefault(courier.Id),
                location?.Position.Y, location?.Position.X, location?.RecordedAtUtc,
                clusterMetric.DistanceKm, clusterMetric.BearingDifferenceDegrees);
        }).ToList();
        CourierAssignmentCriteria criteria = new(order.BranchId, branch.Latitude, branch.Longitude,
            settings.PreferBranchCouriers, settings.MaxActiveOrdersPerCourier, settings.RequireFreshLocation,
            settings.LocationFreshnessMinutes, settings.AssignmentRadiusKm, timeProvider.GetUtcNow(),
            settings.PreferDeliveryClusters, settings.DeliveryClusterRadiusKm,
            settings.DeliveryClusterMaxBearingDegrees);

        List<CourierSuggestionResponse> result = [];
        while (remaining.Count > 0)
        {
            CourierAssignmentCandidate? selected = CourierAssignmentRanker.SelectBest(remaining, criteria);
            if (selected is null) break;
            Courier courier = courierLookup[selected.CourierId];
            double? distance = CourierAssignmentRanker.CalculateDistanceKm(selected.Latitude, selected.Longitude,
                branch.Latitude, branch.Longitude);
            result.Add(new CourierSuggestionResponse(result.Count + 1, courier.Id,
                $"{courier.FirstName} {courier.LastName}", courier.BranchId, courier.BranchId == order.BranchId,
                selected.ActiveOrderCount, distance.HasValue ? Math.Round(distance.Value, 2) : null,
                selected.LocationRecordedAtUtc,
                selected.NearestActiveDeliveryDistanceKm.HasValue &&
                selected.NearestActiveDeliveryDistanceKm.Value != double.MaxValue
                    ? Math.Round(selected.NearestActiveDeliveryDistanceKm.Value, 2)
                    : null,
                selected.NearestActiveDeliveryBearingDifferenceDegrees.HasValue
                    ? Math.Round(selected.NearestActiveDeliveryBearingDifferenceDegrees.Value, 1)
                    : null));
            remaining.Remove(selected);
        }
        return result;
    }

    private static double ResolveShortestRoadDistance(double forwardKm, double reverseKm) =>
        Math.Min(forwardKm, reverseKm);

    private sealed record PreliminaryRouteMatch(Guid CourierId, double Latitude, double Longitude,
        double? AirDistanceKm, double? BearingDifferenceDegrees);
}

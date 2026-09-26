using DeliveryOps.BuildingBlocks.Domain;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class BusinessDispatchSettings : Entity
{
    private BusinessDispatchSettings() { }

    private BusinessDispatchSettings(Guid businessId)
    {
        BusinessId = businessId;
    }

    public Guid BusinessId { get; private init; }
    public bool AutoConfirmOrders { get; private set; }
    public bool AutoAssignCouriers { get; private set; }
    public bool AllowCourierSelfClaim { get; private set; } = true;
    public bool PreferBranchCouriers { get; private set; } = true;
    public int MaxActiveOrdersPerCourier { get; private set; } = 2;
    public bool RequireFreshLocation { get; private set; } = true;
    public int LocationFreshnessMinutes { get; private set; } = 5;
    public double? AssignmentRadiusKm { get; private set; } = 10;
    public bool PreferDeliveryClusters { get; private set; } = true;
    public double DeliveryClusterRadiusKm { get; private set; } = 2;
    public double DeliveryClusterMaxBearingDegrees { get; private set; } = 45;

    public static BusinessDispatchSettings CreateDefault(Guid businessId)
    {
        if (businessId == Guid.Empty) throw new ArgumentException("Business is required.", nameof(businessId));
        return new BusinessDispatchSettings(businessId);
    }

    public void Update(bool autoConfirmOrders, bool autoAssignCouriers, bool allowCourierSelfClaim,
        bool preferBranchCouriers, int maxActiveOrdersPerCourier, bool requireFreshLocation,
        int locationFreshnessMinutes, double? assignmentRadiusKm, bool preferDeliveryClusters,
        double deliveryClusterRadiusKm, double deliveryClusterMaxBearingDegrees)
    {
        if (maxActiveOrdersPerCourier is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(maxActiveOrdersPerCourier));
        if (locationFreshnessMinutes is < 1 or > 120)
            throw new ArgumentOutOfRangeException(nameof(locationFreshnessMinutes));
        if (assignmentRadiusKm is <= 0 or > 200)
            throw new ArgumentOutOfRangeException(nameof(assignmentRadiusKm));
        if (deliveryClusterRadiusKm is < 0.1 or > 25)
            throw new ArgumentOutOfRangeException(nameof(deliveryClusterRadiusKm));
        if (deliveryClusterMaxBearingDegrees is < 5 or > 180)
            throw new ArgumentOutOfRangeException(nameof(deliveryClusterMaxBearingDegrees));

        AutoAssignCouriers = autoAssignCouriers;
        AutoConfirmOrders = autoConfirmOrders || autoAssignCouriers;
        AllowCourierSelfClaim = allowCourierSelfClaim;
        PreferBranchCouriers = preferBranchCouriers;
        MaxActiveOrdersPerCourier = maxActiveOrdersPerCourier;
        RequireFreshLocation = requireFreshLocation;
        LocationFreshnessMinutes = locationFreshnessMinutes;
        AssignmentRadiusKm = assignmentRadiusKm;
        PreferDeliveryClusters = preferDeliveryClusters;
        DeliveryClusterRadiusKm = deliveryClusterRadiusKm;
        DeliveryClusterMaxBearingDegrees = deliveryClusterMaxBearingDegrees;
        MarkAsUpdated();
    }
}

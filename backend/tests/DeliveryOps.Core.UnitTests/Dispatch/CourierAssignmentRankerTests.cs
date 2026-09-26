using DeliveryOps.Core.Domain.Dispatch;

namespace DeliveryOps.Core.UnitTests.Dispatch;

public sealed class CourierAssignmentRankerTests
{
    [Fact]
    public void SelectBest_PrefersBranchCourierBeforeCloserSharedCourier()
    {
        Guid branchId = Guid.NewGuid();
        CourierAssignmentCandidate branchCourier = new(Guid.NewGuid(), branchId, 0, 41.02, 29.02, DateTimeOffset.UtcNow);
        CourierAssignmentCandidate sharedCourier = new(Guid.NewGuid(), null, 0, 41.001, 29.001, DateTimeOffset.UtcNow);
        CourierAssignmentCriteria criteria = new(branchId, 41, 29, true, 2, true, 5, 20, DateTimeOffset.UtcNow);

        CourierAssignmentCandidate? selected = CourierAssignmentRanker.SelectBest([sharedCourier, branchCourier], criteria);

        Assert.Equal(branchCourier.CourierId, selected?.CourierId);
    }

    [Fact]
    public void SelectBest_ExcludesCourierAtCapacityAndWithStaleLocation()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CourierAssignmentCandidate atCapacity = new(Guid.NewGuid(), null, 2, 41, 29, now);
        CourierAssignmentCandidate stale = new(Guid.NewGuid(), null, 0, 41, 29, now.AddMinutes(-10));
        CourierAssignmentCandidate eligible = new(Guid.NewGuid(), null, 1, 41.01, 29.01, now.AddMinutes(-1));
        CourierAssignmentCriteria criteria = new(Guid.NewGuid(), 41, 29, false, 2, true, 5, 20, now);

        CourierAssignmentCandidate? selected = CourierAssignmentRanker.SelectBest([atCapacity, stale, eligible], criteria);

        Assert.Equal(eligible.CourierId, selected?.CourierId);
    }

    [Fact]
    public void SelectBest_ReturnsNullWhenCourierIsOutsideRadius()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CourierAssignmentCandidate candidate = new(Guid.NewGuid(), null, 0, 42, 30, now);
        CourierAssignmentCriteria criteria = new(Guid.NewGuid(), 41, 29, false, 2, true, 5, 5, now);

        Assert.Null(CourierAssignmentRanker.SelectBest([candidate], criteria));
    }

    [Fact]
    public void SelectBest_PrefersCourierAlreadyDeliveringToSameArea()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid branchId = Guid.NewGuid();
        CourierAssignmentCandidate idleCourier = new(Guid.NewGuid(), branchId, 0, 41, 29, now, null);
        CourierAssignmentCandidate clusteredCourier = new(Guid.NewGuid(), branchId, 1, 41.01, 29.01, now, 0.8);
        CourierAssignmentCriteria criteria = new(branchId, 41, 29, false, 3, true, 5, 20, now, true, 2);

        CourierAssignmentCandidate? selected = CourierAssignmentRanker.SelectBest([idleCourier, clusteredCourier], criteria);

        Assert.Equal(clusteredCourier.CourierId, selected?.CourierId);
    }

    [Fact]
    public void SelectBest_DoesNotClusterOutsideConfiguredRadius()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid branchId = Guid.NewGuid();
        CourierAssignmentCandidate idleCourier = new(Guid.NewGuid(), branchId, 0, 41, 29, now, null);
        CourierAssignmentCandidate distantCourier = new(Guid.NewGuid(), branchId, 1, 41.01, 29.01, now, 3);
        CourierAssignmentCriteria criteria = new(branchId, 41, 29, false, 3, true, 5, 20, now, true, 2);

        CourierAssignmentCandidate? selected = CourierAssignmentRanker.SelectBest([distantCourier, idleCourier], criteria);

        Assert.Equal(idleCourier.CourierId, selected?.CourierId);
    }
}

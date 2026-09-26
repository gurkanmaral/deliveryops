using DeliveryOps.Core.Domain.Entities;

namespace DeliveryOps.Core.UnitTests.Domain;

public sealed class BusinessDispatchSettingsTests
{
    [Fact]
    public void CreateDefault_UsesSafeLocationLimits()
    {
        BusinessDispatchSettings settings = BusinessDispatchSettings.CreateDefault(Guid.NewGuid());

        Assert.True(settings.RequireFreshLocation);
        Assert.Equal(5, settings.LocationFreshnessMinutes);
        Assert.Equal(10, settings.AssignmentRadiusKm);
    }

    [Fact]
    public void Update_EnablesAutoConfirmationWhenAutoAssignmentIsEnabled()
    {
        BusinessDispatchSettings settings = BusinessDispatchSettings.CreateDefault(Guid.NewGuid());

        settings.Update(false, true, true, true, 3, false, 5, null, true, 2, 45);

        Assert.True(settings.AutoConfirmOrders);
        Assert.True(settings.AutoAssignCouriers);
        Assert.Equal(3, settings.MaxActiveOrdersPerCourier);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Update_RejectsInvalidCourierCapacity(int capacity)
    {
        BusinessDispatchSettings settings = BusinessDispatchSettings.CreateDefault(Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            settings.Update(false, false, true, true, capacity, false, 5, null, true, 2, 45));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25.1)]
    public void Update_RejectsInvalidDeliveryClusterRadius(double radiusKm)
    {
        BusinessDispatchSettings settings = BusinessDispatchSettings.CreateDefault(Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            settings.Update(false, false, true, true, 2, false, 5, null, true, radiusKm, 45));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(181)]
    public void Update_RejectsInvalidDeliveryClusterBearing(double bearingDegrees)
    {
        BusinessDispatchSettings settings = BusinessDispatchSettings.CreateDefault(Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            settings.Update(false, false, true, true, 2, false, 5, null, true, 2, bearingDegrees));
    }
}

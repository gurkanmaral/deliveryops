using DeliveryOps.Core.Domain.Entities;

namespace DeliveryOps.Core.UnitTests.Domain;

public sealed class BusinessDispatchSettingsTests
{
    [Fact]
    public void Update_EnablesAutoConfirmationWhenAutoAssignmentIsEnabled()
    {
        BusinessDispatchSettings settings = BusinessDispatchSettings.CreateDefault(Guid.NewGuid());

        settings.Update(false, true, true, true, 3, false, 5, null);

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
            settings.Update(false, false, true, true, capacity, false, 5, null));
    }
}

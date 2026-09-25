using DeliveryOps.BuildingBlocks.Domain;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class BusinessSlaSettings : Entity
{
    private BusinessSlaSettings() { }
    private BusinessSlaSettings(Guid businessId) => BusinessId = businessId;

    public Guid BusinessId { get; private init; }
    public int CourierWaitingWarningMinutes { get; private set; } = 5;
    public int CourierWaitingCriticalMinutes { get; private set; } = 10;
    public int PickupWarningMinutes { get; private set; } = 10;
    public int PickupCriticalMinutes { get; private set; } = 20;
    public int DeliveryWarningMinutes { get; private set; } = 30;
    public int DeliveryCriticalMinutes { get; private set; } = 45;
    public int LocationStaleWarningMinutes { get; private set; } = 3;
    public int LocationStaleCriticalMinutes { get; private set; } = 10;

    public static BusinessSlaSettings CreateDefault(Guid businessId)
    {
        if (businessId == Guid.Empty) throw new ArgumentException("Business is required.", nameof(businessId));
        return new BusinessSlaSettings(businessId);
    }

    public void Update(int courierWaitingWarningMinutes, int courierWaitingCriticalMinutes,
        int pickupWarningMinutes, int pickupCriticalMinutes, int deliveryWarningMinutes,
        int deliveryCriticalMinutes, int locationStaleWarningMinutes, int locationStaleCriticalMinutes)
    {
        ValidatePair(courierWaitingWarningMinutes, courierWaitingCriticalMinutes, nameof(courierWaitingWarningMinutes));
        ValidatePair(pickupWarningMinutes, pickupCriticalMinutes, nameof(pickupWarningMinutes));
        ValidatePair(deliveryWarningMinutes, deliveryCriticalMinutes, nameof(deliveryWarningMinutes));
        ValidatePair(locationStaleWarningMinutes, locationStaleCriticalMinutes, nameof(locationStaleWarningMinutes));
        CourierWaitingWarningMinutes = courierWaitingWarningMinutes;
        CourierWaitingCriticalMinutes = courierWaitingCriticalMinutes;
        PickupWarningMinutes = pickupWarningMinutes;
        PickupCriticalMinutes = pickupCriticalMinutes;
        DeliveryWarningMinutes = deliveryWarningMinutes;
        DeliveryCriticalMinutes = deliveryCriticalMinutes;
        LocationStaleWarningMinutes = locationStaleWarningMinutes;
        LocationStaleCriticalMinutes = locationStaleCriticalMinutes;
        MarkAsUpdated();
    }

    private static void ValidatePair(int warning, int critical, string parameterName)
    {
        if (warning is < 1 or > 1440 || critical is < 1 or > 1440 || critical <= warning)
            throw new ArgumentOutOfRangeException(parameterName, "Uyarı süresi 1-1440 dakika arasında, kritik süre ise uyarı süresinden büyük olmalıdır.");
    }
}

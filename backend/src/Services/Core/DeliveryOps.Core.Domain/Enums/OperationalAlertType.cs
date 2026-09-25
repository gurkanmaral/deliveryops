namespace DeliveryOps.Core.Domain.Enums;

public enum OperationalAlertType
{
    CourierWaiting,
    PickupDelayed,
    DeliveryDelayed,
    CourierLocationStale,
    LowCreditBalance,
    IntegrationConnectionUnavailable
}

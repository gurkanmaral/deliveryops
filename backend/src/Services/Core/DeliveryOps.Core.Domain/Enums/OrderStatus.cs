namespace DeliveryOps.Core.Domain.Enums;

public enum OrderStatus
{
    New,
    Confirmed,
    WaitingForCourier,
    Assigned,
    PickedUp,
    OnTheWay,
    Delivered,
    Cancelled,
    DeliveryFailed,
    Returned
}

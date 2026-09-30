namespace DeliveryOps.Core.Domain.Enums;

/// <summary>How the customer pays. Online means the provider or web shop already charged the customer.</summary>
public enum PaymentMethod
{
    Unspecified = 0,
    Online = 1,
    Cash = 2,
    Card = 3
}

public enum PaymentStatus
{
    Unpaid = 0,
    Paid = 1
}

/// <summary>Where the money was taken.</summary>
public enum PaymentChannel
{
    Counter = 0,
    Courier = 1,
    Provider = 2,
    Panel = 3
}

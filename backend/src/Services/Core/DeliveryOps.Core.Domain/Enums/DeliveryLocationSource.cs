namespace DeliveryOps.Core.Domain.Enums;

public enum DeliveryLocationSource
{
    Unknown = 0,
    Provider = 1,
    MapPin = 2,
    Geocoded = 3
}

public enum DeliveryLocationAccuracy
{
    Unknown = 0,
    Exact = 1,
    Approximate = 2
}

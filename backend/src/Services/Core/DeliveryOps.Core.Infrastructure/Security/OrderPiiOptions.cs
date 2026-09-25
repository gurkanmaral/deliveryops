namespace DeliveryOps.Core.Infrastructure.Security;

public sealed class OrderPiiOptions
{
    public string SearchKey { get; init; } = string.Empty;
    public string? KeyRingPath { get; init; }
}

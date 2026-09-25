using DeliveryOps.BuildingBlocks.Domain;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class ProviderOrderEventReceipt : Entity
{
    private ProviderOrderEventReceipt() { }

    public Guid BusinessId { get; private init; }
    public Guid OrderId { get; private init; }
    public OrderSource Source { get; private init; }
    public string ExternalEventId { get; private init; } = string.Empty;
    public string ProviderStatus { get; private init; } = string.Empty;
    public string Outcome { get; private init; } = string.Empty;

    public static ProviderOrderEventReceipt Create(Guid businessId, Guid orderId, OrderSource source,
        string externalEventId, string providerStatus, string outcome)
    {
        if (businessId == Guid.Empty || orderId == Guid.Empty)
            throw new ArgumentException("Business and order are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(externalEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerStatus);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        return new ProviderOrderEventReceipt
        {
            BusinessId = businessId, OrderId = orderId, Source = source,
            ExternalEventId = externalEventId.Trim(), ProviderStatus = providerStatus.Trim(),
            Outcome = outcome.Trim()
        };
    }
}

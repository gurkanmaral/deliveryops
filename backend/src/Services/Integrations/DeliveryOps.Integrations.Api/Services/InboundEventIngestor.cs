using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Integrations.Api.Services;

public enum IngestOutcome { Accepted, Duplicate, Conflict }

public sealed record IngestResult(IngestOutcome Outcome, InboundOrderEvent? Event);

/// <summary>
/// Stores an adapted provider payload as an inbound event exactly once per (connection, external event id).
/// Used by the webhook endpoint and by providers that are polled instead of pushing webhooks.
/// </summary>
public sealed class InboundEventIngestor(IntegrationsDbContext context)
{
    public async Task<IngestResult> IngestAsync(IntegrationConnection connection, AdaptedOrder adapted,
        string rawPayload, CancellationToken cancellationToken)
    {
        InboundOrderEvent? existing = await FindAsync(connection.Id, adapted.ExternalEventId, cancellationToken);
        if (existing is not null) return Existing(existing, adapted);

        InboundOrderEvent inboundEvent = InboundOrderEvent.Receive(connection.Id, adapted.ExternalEventId,
            adapted.Order.ExternalOrderId, adapted.EventType, connection.AdapterVersion,
            adapted.PayloadHash, rawPayload, adapted.NormalizedPayload);
        context.InboundOrderEvents.Add(inboundEvent);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new IngestResult(IngestOutcome.Accepted, inboundEvent);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent delivery of the same event.
            context.ChangeTracker.Clear();
            existing = await FindAsync(connection.Id, adapted.ExternalEventId, cancellationToken)
                       ?? throw new InvalidOperationException("Inbound event could not be stored.");
            return Existing(existing, adapted);
        }
    }

    private Task<InboundOrderEvent?> FindAsync(Guid connectionId, string externalEventId,
        CancellationToken cancellationToken) =>
        context.InboundOrderEvents.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ConnectionId == connectionId && item.ExternalEventId == externalEventId, cancellationToken);

    private static IngestResult Existing(InboundOrderEvent existing, AdaptedOrder adapted) =>
        string.Equals(existing.PayloadHash, adapted.PayloadHash, StringComparison.Ordinal)
            ? new IngestResult(IngestOutcome.Duplicate, existing)
            : new IngestResult(IngestOutcome.Conflict, existing);
}

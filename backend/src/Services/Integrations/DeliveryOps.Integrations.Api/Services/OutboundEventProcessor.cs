using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DeliveryOps.Integrations.Api.Services;

public sealed class OutboundEventProcessor(IntegrationsDbContext context,
    YemeksepetiPartnerClient yemeksepetiClient, TimeProvider timeProvider,
    ILogger<OutboundEventProcessor> logger)
{
    private const int MaxAttempts = 5;

    public async Task ProcessAsync(Guid eventId, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({$"outbound-event:{eventId}"}, 0))", cancellationToken);
        OutboundOrderEvent? item = await context.OutboundOrderEvents.Include(x => x.Connection)
            .SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (item is null || item.Status is OutboundEventStatus.Completed or OutboundEventStatus.DeadLettered) return;
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (item.Status == OutboundEventStatus.Processing && item.LastAttemptAtUtc > now.AddMinutes(-2)) return;
        item.StartProcessing(now);
        await context.SaveChangesAsync(cancellationToken);
        try
        {
            if (!item.Connection.IsActive) throw new InvalidOperationException("Entegrasyon bağlantısı pasif.");
            InboundOrderEvent inbound = await context.InboundOrderEvents.AsNoTracking()
                .SingleAsync(x => x.ConnectionId == item.ConnectionId && x.CoreOrderId == item.CoreOrderId,
                    cancellationToken);
            await yemeksepetiClient.UpdateOrderStatusAsync(item.Connection, item.ExternalOrderId,
                item.ProviderStatus, item.CancellationReason, inbound.RawPayload, cancellationToken);
            item.Complete(timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
                                           InvalidOperationException or JsonException)
        {
            item.Fail(exception.Message, timeProvider.GetUtcNow(), MaxAttempts);
            logger.LogWarning(exception, "Outbound event {EventId} attempt {Attempt} failed.", item.Id, item.Attempts);
        }
        await context.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }
}

using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Integrations.Api.Services;

public sealed class OutboundEventWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    ILogger<OutboundEventWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(3), timeProvider);
        do
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Outbound event worker failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        Guid[] ids;
        DateTimeOffset now = timeProvider.GetUtcNow();
        await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
        {
            IntegrationsDbContext context = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();
            ids = await context.OutboundOrderEvents.AsNoTracking()
                .Where(x => (x.Status == OutboundEventStatus.Pending || x.Status == OutboundEventStatus.Failed) &&
                            x.NextAttemptAtUtc <= now || x.Status == OutboundEventStatus.Processing &&
                            x.LastAttemptAtUtc < now.AddMinutes(-2))
                .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.CreatedAtUtc)
                .Select(x => x.Id).Take(20).ToArrayAsync(cancellationToken);
        }
        foreach (Guid id in ids)
        {
            // One failing event must not stop the rest of the batch.
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutboundEventProcessor>()
                    .ProcessAsync(id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger.LogError(exception, "Outbound event {EventId} could not be processed.", id); }
        }
    }
}

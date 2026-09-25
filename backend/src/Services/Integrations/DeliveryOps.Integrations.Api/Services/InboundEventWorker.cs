using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Integrations.Api.Services;

public sealed class InboundEventWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    ILogger<InboundEventWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(3), timeProvider);
        do
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Inbound event retry worker failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        Guid[] ids;
        DateTimeOffset now = timeProvider.GetUtcNow();
        await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
        {
            IntegrationsDbContext context = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();
            ids = await context.InboundOrderEvents.AsNoTracking()
                .Where(x => (x.Status == InboundEventStatus.Received || x.Status == InboundEventStatus.Failed) &&
                            x.NextAttemptAtUtc <= now ||
                            x.Status == InboundEventStatus.Processing && x.LastAttemptAtUtc < now.AddMinutes(-2))
                .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.ReceivedAtUtc)
                .Select(x => x.Id).Take(20).ToArrayAsync(cancellationToken);
        }
        foreach (Guid id in ids)
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<InboundEventProcessor>().ProcessAsync(id, cancellationToken);
        }
    }
}

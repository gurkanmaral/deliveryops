using System.Collections.Concurrent;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Integrations.Api.Services;

/// <summary>
/// Trendyol Go – Yemek does not push meal orders, so every active Trendyol connection with API keys is polled
/// for packages modified since the last successful read. Each package status becomes one inbound event
/// (package id + status is the idempotency key), then flows through the same pipeline as webhooks.
/// </summary>
public sealed class TrendyolOrderPollingWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    IConfiguration configuration, ILogger<TrendyolOrderPollingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan InitialLookback = TimeSpan.FromHours(3);
    // Modification dates can be written slightly after the change; re-read a short overlap every poll.
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(2);
    private const int MaxPagesPerPoll = 10;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _cursors = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int seconds = Math.Clamp(configuration.GetValue("Trendyol:PollIntervalSeconds", 15), 5, 300);
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(seconds), timeProvider);
        do
        {
            try { await PollAllAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Trendyol order polling failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAllAsync(CancellationToken cancellationToken)
    {
        Guid[] connectionIds;
        await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
        {
            IntegrationsDbContext context = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();
            connectionIds = await context.Connections.AsNoTracking()
                .Where(x => x.IsActive && x.Provider == IntegrationProvider.Trendyol &&
                            x.ProtectedCredentials != null && x.ProviderAccountId != null)
                .Select(x => x.Id).ToArrayAsync(cancellationToken);
        }
        foreach (Guid id in connectionIds)
        {
            // One broken connection (wrong keys, IP not authorised) must not block the others.
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                await PollConnectionAsync(scope.ServiceProvider, id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Trendyol packages could not be read for connection {ConnectionId}.", id);
            }
        }
    }

    private async Task PollConnectionAsync(IServiceProvider services, Guid connectionId,
        CancellationToken cancellationToken)
    {
        IntegrationsDbContext context = services.GetRequiredService<IntegrationsDbContext>();
        IntegrationConnection? connection = await context.Connections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == connectionId, cancellationToken);
        if (connection is null) return;
        TrendyolGoClient client = services.GetRequiredService<TrendyolGoClient>();
        IOrderProviderAdapter adapter = services.GetRequiredService<ProviderAdapterRegistry>()
            .Resolve(connection.Provider, connection.AdapterVersion);
        InboundEventIngestor ingestor = services.GetRequiredService<InboundEventIngestor>();

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset from = _cursors.TryGetValue(connectionId, out DateTimeOffset cursor) ? cursor : now - InitialLookback;
        int accepted = 0;
        bool complete = false;
        for (int page = 0; page < MaxPagesPerPoll; page++)
        {
            TrendyolPackagePage result = await client.GetPackagesAsync(connection, from.ToUnixTimeMilliseconds(),
                now.ToUnixTimeMilliseconds(), page, cancellationToken);
            foreach (string package in result.Packages)
            {
                try
                {
                    AdaptedOrder adapted = adapter.Adapt(package);
                    // Status changes of orders we never imported (e.g. finished before the keys were entered)
                    // would only fail and dead-letter.
                    if (adapted.EventType != "order.created" && !await context.InboundOrderEvents.AnyAsync(x =>
                            x.ConnectionId == connectionId && x.ExternalOrderId == adapted.Order.ExternalOrderId &&
                            x.EventType == "order.created", cancellationToken))
                        continue;
                    IngestResult ingested = await ingestor.IngestAsync(connection, adapted, package, cancellationToken);
                    if (ingested.Outcome == IngestOutcome.Accepted) accepted++;
                }
                catch (ProviderPayloadException exception)
                {
                    logger.LogWarning(exception, "Trendyol package skipped for connection {ConnectionId}.", connectionId);
                }
            }
            if (page + 1 >= result.TotalPages) { complete = true; break; }
        }
        // Advance only after every page was read; otherwise the next poll starts from the same point.
        if (complete) _cursors[connectionId] = now - Overlap;
        if (accepted > 0)
            logger.LogInformation("Trendyol connection {ConnectionId}: {Count} new package events.", connectionId, accepted);
    }
}

using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeliveryOps.Integrations.Api.Services;

public sealed class IntegrationConnectionHealthWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<IntegrationHealthCheckOptions> options,
    ILogger<IntegrationConnectionHealthWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Automatic integration health checks are disabled.");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        TimeSpan scanInterval = TimeSpan.FromSeconds(Math.Clamp(
            options.Value.ScanIntervalSeconds, 10, 3600));
        using PeriodicTimer timer = new(scanInterval, timeProvider);
        do
        {
            try { await ProcessDueConnectionsAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic integration health check scan failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessDueConnectionsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset dueBefore = now.AddMinutes(-Math.Clamp(options.Value.IntervalMinutes, 1, 1440));
        Guid[] ids;
        await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
        {
            IntegrationsDbContext context = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();
            ids = await context.Connections.AsNoTracking()
                .Where(x => x.IsActive && (x.Provider == IntegrationProvider.Yemeksepeti || x.Provider == IntegrationProvider.Getir) &&
                            x.ProtectedCredentials != null && x.ProviderAccountId != null &&
                            (x.LastAutomaticHealthCheckAtUtc == null ||
                             x.LastAutomaticHealthCheckAtUtc <= dueBefore))
                .OrderBy(x => x.LastAutomaticHealthCheckAtUtc)
                .Select(x => x.Id)
                .Take(20)
                .ToArrayAsync(cancellationToken);
        }

        foreach (Guid id in ids)
        {
            try { await CheckClaimedConnectionAsync(id, dueBefore, now, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic health check failed for connection {ConnectionId}.", id);
            }
        }
    }

    private async Task CheckClaimedConnectionAsync(Guid id, DateTimeOffset dueBefore,
        DateTimeOffset claimedAt, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IntegrationsDbContext context = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();
        int claimed = await context.Connections
            .Where(x => x.Id == id && x.IsActive &&
                        (x.LastAutomaticHealthCheckAtUtc == null ||
                         x.LastAutomaticHealthCheckAtUtc <= dueBefore))
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                x => x.LastAutomaticHealthCheckAtUtc, claimedAt), cancellationToken);
        if (claimed == 0) return;

        IntegrationConnection connection = await context.Connections.SingleAsync(x => x.Id == id,
            cancellationToken);
        IntegrationConnectionHealthChecker checker = scope.ServiceProvider
            .GetRequiredService<IntegrationConnectionHealthChecker>();
        await checker.CheckAsync(connection, automatic: true, cancellationToken);
    }
}

using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeliveryOps.Integrations.Api.Services;

public sealed class IntegrationRetentionWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<IntegrationRetentionOptions> options,
    ILogger<IntegrationRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        using PeriodicTimer timer = new(TimeSpan.FromHours(Math.Clamp(
            options.Value.ScanIntervalHours, 1, 168)), timeProvider);
        do
        {
            try { await PurgeAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Integration retention cleanup failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset cutoff = now.AddDays(-Math.Clamp(
            options.Value.RetentionDays, 7, 3650));
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IntegrationsDbContext context = scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>();
        int inbound = await context.InboundOrderEvents
            .Where(x => x.Status == InboundEventStatus.Completed && x.ProcessedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        int outbound = await context.OutboundOrderEvents
            .Where(x => x.Status == OutboundEventStatus.Completed && x.ProcessedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        int healthChecks = await context.HealthChecks.Where(x => x.CheckedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        int expiredSecrets = await context.Connections
            .Where(x => x.PreviousSecretValidUntilUtc != null && x.PreviousSecretValidUntilUtc <= now)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.PreviousSecretHash, (string?)null)
                .SetProperty(x => x.PreviousProtectedSecret, (string?)null)
                .SetProperty(x => x.PreviousSecretValidUntilUtc, (DateTimeOffset?)null), cancellationToken);
        if (inbound + outbound + healthChecks + expiredSecrets > 0)
            logger.LogInformation(
                "Integration retention removed {Inbound} inbound, {Outbound} outbound, {HealthChecks} health rows and {ExpiredSecrets} expired webhook secrets.",
                inbound, outbound, healthChecks, expiredSecrets);
    }
}

public sealed class IntegrationRetentionOptions
{
    public bool Enabled { get; init; } = true;
    public int RetentionDays { get; init; } = 90;
    public int ScanIntervalHours { get; init; } = 24;
}

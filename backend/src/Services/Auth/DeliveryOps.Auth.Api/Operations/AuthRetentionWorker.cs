using DeliveryOps.Auth.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DeliveryOps.Auth.Api.Operations;

public sealed class AuthRetentionWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    IOptions<AuthRetentionOptions> options, ILogger<AuthRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        using PeriodicTimer timer = new(TimeSpan.FromHours(Math.Clamp(options.Value.ScanIntervalHours, 1, 168)), timeProvider);
        do
        {
            try
            {
                DateTimeOffset cutoff = timeProvider.GetUtcNow()
                    .AddDays(-Math.Clamp(options.Value.ExpiredRefreshTokenDays, 1, 365));
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                AuthDbContext context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                int deleted = await context.RefreshTokens
                    .Where(token => token.ExpiresAtUtc < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Auth retention removed {Count} expired refresh tokens.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Auth retention cleanup failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class AuthRetentionOptions
{
    public bool Enabled { get; init; } = true;
    public int ExpiredRefreshTokenDays { get; init; } = 30;
    public int ScanIntervalHours { get; init; } = 24;
}

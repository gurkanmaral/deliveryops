using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.Core.Queries.Locations;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Api.Realtime;

public sealed class CourierPresenceMonitor(IServiceScopeFactory scopeFactory, IConfiguration configuration,
    ILogger<CourierPresenceMonitor> logger) : BackgroundService
{
    private readonly HashSet<Guid> _reportedStale = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                CoreDbContext database = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
                IOperationsNotifier notifier = scope.ServiceProvider.GetRequiredService<IOperationsNotifier>();
                DateTimeOffset threshold = DateTimeOffset.UtcNow.AddSeconds(-configuration.GetValue("CourierTracking:StaleAfterSeconds", 120));
                var couriers = await database.Couriers.AsNoTracking().Where(x => x.IsActive && x.LastLocationAtUtc != null)
                    .Select(x => new { x.Id, x.BusinessId, x.LastLocationAtUtc }).ToListAsync(stoppingToken);
                foreach (var courier in couriers)
                {
                    if (courier.LastLocationAtUtc < threshold && _reportedStale.Add(courier.Id))
                        await notifier.LocationStaleAsync(courier.Id, courier.BusinessId, stoppingToken);
                    else if (courier.LastLocationAtUtc >= threshold)
                        _reportedStale.Remove(courier.Id);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Courier stale-location monitoring failed.");
            }
        }
    }
}

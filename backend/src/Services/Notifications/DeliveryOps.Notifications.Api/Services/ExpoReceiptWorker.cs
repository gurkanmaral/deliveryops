using DeliveryOps.Notifications.Api.Domain;
using DeliveryOps.Notifications.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Notifications.Api.Services;

public sealed class ExpoReceiptWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    ILogger<ExpoReceiptWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckReceipts(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Expo receipt check failed."); }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task CheckReceipts(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        NotificationsDbContext database = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        ExpoPushService pushService = scope.ServiceProvider.GetRequiredService<ExpoPushService>();
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<ExpoPushReceipt> pending = await database.ExpoPushReceipts
            .Where(x => x.CompletedAtUtc == null && x.NextCheckAtUtc <= now)
            .OrderBy(x => x.CreatedAtUtc).Take(300).ToListAsync(cancellationToken);
        if (pending.Count == 0) return;

        IReadOnlyDictionary<string, PushReceiptResult> results = await pushService.GetReceiptsAsync(
            pending.Select(x => x.TicketId).ToArray(), cancellationToken);
        foreach (ExpoPushReceipt receipt in pending)
        {
            if (!results.TryGetValue(receipt.TicketId, out PushReceiptResult? result)) { receipt.Retry(now); continue; }
            receipt.Complete(result.Error, now);
            if (result.Error == "DeviceNotRegistered")
            {
                PushDevice? device = await database.PushDevices.SingleOrDefaultAsync(
                    x => x.ExpoPushToken == receipt.ExpoPushToken, cancellationToken);
                device?.Deactivate();
            }
        }
        await database.SaveChangesAsync(cancellationToken);
    }
}

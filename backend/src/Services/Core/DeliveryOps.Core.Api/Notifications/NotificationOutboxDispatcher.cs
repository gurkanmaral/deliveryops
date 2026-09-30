using System.Text;
using System.Text.Json.Nodes;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DeliveryOps.Core.Api.Operations;

namespace DeliveryOps.Core.Api.Notifications;

public sealed class NotificationOutboxDispatcher(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider, IOptions<NotificationOutboxOptions> options,
    ILogger<NotificationOutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(3));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchBatch(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Notification outbox batch failed."); }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task DispatchBatch(CancellationToken cancellationToken)
    {
        Guid[] candidates;
        DateTimeOffset now = timeProvider.GetUtcNow();
        await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
        {
            CoreDbContext database = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            candidates = await database.NotificationOutbox.AsNoTracking()
                .Where(x => x.ProcessedAtUtc == null && x.DeadLetteredAtUtc == null && x.NextAttemptAtUtc <= now &&
                    (x.ProcessingAtUtc == null || x.ProcessingAtUtc < now.AddMinutes(-2)))
                .OrderBy(x => x.CreatedAtUtc).Select(x => x.Id).Take(20).ToArrayAsync(cancellationToken);
        }

        foreach (Guid id in candidates)
        {
            try { await DispatchOne(id, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger.LogError(exception, "Notification event {EventId} could not be dispatched.", id); }
        }
    }

    private async Task DispatchOne(Guid id, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CoreDbContext database = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        int claimed = await database.NotificationOutbox
            .Where(x => x.Id == id && x.ProcessedAtUtc == null && x.DeadLetteredAtUtc == null &&
                (x.ProcessingAtUtc == null || x.ProcessingAtUtc < now.AddMinutes(-2)))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProcessingAtUtc, now), cancellationToken);
        if (claimed == 0) return;
        NotificationOutboxMessage message = await database.NotificationOutbox.SingleAsync(x => x.Id == id, cancellationToken);

        try
        {
            string payload = await WithEligibleCouriersAsync(database, message, cancellationToken);
            using StringContent content = new(payload, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await httpClientFactory.CreateClient("Notifications")
                .PostAsync("internal/v1/notifications/order-event", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                message.MarkProcessed(timeProvider.GetUtcNow());
            }
            else
            {
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                string error = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {responseBody}";
                bool retryable = OutboxHttpPolicy.IsRetryable(response.StatusCode);
                message.MarkFailed(error, timeProvider.GetUtcNow(), options.Value.MaxAttempts,
                    (int)response.StatusCode, permanent: !retryable);
                LogFailure(message, id, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            message.ReleaseForShutdown(timeProvider.GetUtcNow());
            await database.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            message.MarkFailed(exception.Message, timeProvider.GetUtcNow(), options.Value.MaxAttempts);
            LogFailure(message, id, exception);
        }
        await database.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// "Package available" pushes go only to couriers who could actually claim it right now: active, on an
    /// open shift, available, and in the order's branch (or unassigned to a branch). Resolved at send time
    /// so a courier who ended the shift after the order was created is not woken up.
    /// </summary>
    private static async Task<string> WithEligibleCouriersAsync(CoreDbContext database,
        NotificationOutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.EventType != "OrderAvailable") return message.PayloadJson;
        if (JsonNode.Parse(message.PayloadJson) is not JsonObject payload) return message.PayloadJson;
        Guid? branchId = payload["BranchId"] is JsonValue branch && branch.TryGetValue(out Guid parsedBranch)
            ? parsedBranch : null;
        Guid[] courierIds = await database.Couriers.AsNoTracking()
            .Where(x => x.BusinessId == message.BusinessId && x.IsActive &&
                        x.Availability == CourierAvailability.Available &&
                        (x.BranchId == null || x.BranchId == branchId) &&
                        database.CourierShifts.Any(shift => shift.CourierId == x.Id && shift.EndedAtUtc == null))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
        payload["EligibleCourierIds"] = new JsonArray(courierIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        return payload.ToJsonString();
    }

    private void LogFailure(NotificationOutboxMessage message, Guid id, Exception? exception)
    {
        if (message.DeadLetteredAtUtc.HasValue)
            logger.LogError(exception, "Notification event {EventId} was dead-lettered after {Attempts} attempts. HTTP status: {StatusCode}.",
                id, message.Attempts, message.LastHttpStatusCode);
        else
            logger.LogWarning(exception, "Notification event {EventId} delivery attempt {Attempt} failed; it will be retried.",
                id, message.Attempts);
    }
}

public sealed class NotificationOutboxOptions
{
    public int MaxAttempts { get; init; } = 8;
}

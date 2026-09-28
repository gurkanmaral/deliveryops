using System.Text;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DeliveryOps.Core.Api.Operations;

namespace DeliveryOps.Core.Api.Integrations;

public sealed class IntegrationOutboxDispatcher(IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory, TimeProvider timeProvider,
    IOptions<IntegrationOutboxOptions> options,
    ILogger<IntegrationOutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(3), timeProvider);
        do
        {
            try { await DispatchBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Integration outbox batch failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        Guid[] ids;
        DateTimeOffset now = timeProvider.GetUtcNow();
        await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
        {
            CoreDbContext database = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            ids = await database.IntegrationOutbox.AsNoTracking()
                .Where(x => x.ProcessedAtUtc == null && x.DeadLetteredAtUtc == null && x.NextAttemptAtUtc <= now &&
                    (x.ProcessingAtUtc == null || x.ProcessingAtUtc < now.AddMinutes(-2)))
                .OrderBy(x => x.CreatedAtUtc).Select(x => x.Id).Take(20).ToArrayAsync(cancellationToken);
        }
        foreach (Guid id in ids)
        {
            try { await DispatchOneAsync(id, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger.LogError(exception, "Integration event {EventId} could not be dispatched.", id); }
        }
    }

    private async Task DispatchOneAsync(Guid id, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CoreDbContext database = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        int claimed = await database.IntegrationOutbox.Where(x => x.Id == id && x.ProcessedAtUtc == null &&
                x.DeadLetteredAtUtc == null &&
                (x.ProcessingAtUtc == null || x.ProcessingAtUtc < now.AddMinutes(-2)))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProcessingAtUtc, now), cancellationToken);
        if (claimed == 0) return;
        IntegrationOutboxMessage message = await database.IntegrationOutbox.SingleAsync(x => x.Id == id, cancellationToken);
        try
        {
            using StringContent content = new(message.PayloadJson, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await httpClientFactory.CreateClient("Integrations")
                .PostAsync("internal/v1/order-status-events", content, cancellationToken);
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

    private void LogFailure(IntegrationOutboxMessage message, Guid id, Exception? exception)
    {
        if (message.DeadLetteredAtUtc.HasValue)
            logger.LogError(exception, "Integration event {EventId} was dead-lettered after {Attempts} attempts. HTTP status: {StatusCode}.",
                id, message.Attempts, message.LastHttpStatusCode);
        else
            logger.LogWarning(exception, "Integration event {EventId} delivery attempt {Attempt} failed; it will be retried.",
                id, message.Attempts);
    }
}

public sealed class IntegrationOutboxOptions
{
    public int MaxAttempts { get; init; } = 8;
}

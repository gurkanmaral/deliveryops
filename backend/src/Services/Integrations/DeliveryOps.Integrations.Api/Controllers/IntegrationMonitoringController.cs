using DeliveryOps.BuildingBlocks.Security;
using DeliveryOps.Integrations.Api.Domain;
using DeliveryOps.Integrations.Api.Persistence;
using DeliveryOps.Integrations.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Integrations.Api.Controllers;

[ApiController]
[Route("api/v1/integrations/monitoring")]
[Authorize(Policy = Permissions.IntegrationsRead)]
public sealed class IntegrationMonitoringController(
    IntegrationsDbContext context,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IntegrationMonitoringResponse>> Get(
        [FromQuery] Guid? businessId,
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        days = days is 1 or 7 or 30 ? days : 7;
        Guid? scopedBusinessId = TenantScope.GetBusinessId(User);
        if (scopedBusinessId.HasValue)
        {
            if (businessId.HasValue && businessId.Value != scopedBusinessId.Value) return Forbid();
            businessId = scopedBusinessId;
        }
        DateTimeOffset to = timeProvider.GetUtcNow();
        DateTimeOffset from = to.AddDays(-days);
        IQueryable<IntegrationConnection> connectionQuery = context.Connections.AsNoTracking();
        if (businessId.HasValue)
            connectionQuery = connectionQuery.Where(x => x.BusinessId == businessId.Value);
        ConnectionInfo[] connections = await connectionQuery.OrderBy(x => x.Name)
            .Select(x => new ConnectionInfo(x.Id, x.BusinessId, x.Name, x.Provider, x.IsActive,
                x.LastHealthCheckSucceeded, x.LastHealthCheckAtUtc))
            .ToArrayAsync(cancellationToken);
        Guid[] connectionIds = connections.Select(x => x.Id).ToArray();

        EventAggregate[] inbound = await context.InboundOrderEvents.AsNoTracking()
            .Where(x => connectionIds.Contains(x.ConnectionId) && x.ReceivedAtUtc >= from &&
                        x.ReceivedAtUtc <= to)
            .GroupBy(x => x.ConnectionId)
            .Select(group => new EventAggregate(group.Key, group.Count(),
                group.Count(x => x.Status == InboundEventStatus.Completed),
                group.Count(x => x.Status == InboundEventStatus.Failed),
                group.Count(x => x.Status == InboundEventStatus.DeadLettered),
                group.Sum(x => x.Attempts > 1 ? x.Attempts - 1 : 0),
                group.Where(x => x.ProcessedAtUtc != null).Average(x =>
                    (double?)(x.ProcessedAtUtc!.Value - x.ReceivedAtUtc).TotalMilliseconds),
                group.Where(x => x.Status == InboundEventStatus.Completed)
                    .Max(x => (DateTimeOffset?)x.ProcessedAtUtc)))
            .ToArrayAsync(cancellationToken);
        EventAggregate[] outbound = await context.OutboundOrderEvents.AsNoTracking()
            .Where(x => connectionIds.Contains(x.ConnectionId) && x.CreatedAtUtc >= from &&
                        x.CreatedAtUtc <= to)
            .GroupBy(x => x.ConnectionId)
            .Select(group => new EventAggregate(group.Key, group.Count(),
                group.Count(x => x.Status == OutboundEventStatus.Completed),
                group.Count(x => x.Status == OutboundEventStatus.Failed),
                group.Count(x => x.Status == OutboundEventStatus.DeadLettered),
                group.Sum(x => x.Attempts > 1 ? x.Attempts - 1 : 0),
                group.Where(x => x.ProcessedAtUtc != null).Average(x =>
                    (double?)(x.ProcessedAtUtc!.Value - x.CreatedAtUtc).TotalMilliseconds),
                group.Where(x => x.Status == OutboundEventStatus.Completed)
                    .Max(x => (DateTimeOffset?)x.ProcessedAtUtc)))
            .ToArrayAsync(cancellationToken);
        HealthAggregate[] health = await context.HealthChecks.AsNoTracking()
            .Where(x => connectionIds.Contains(x.ConnectionId) && x.CheckedAtUtc >= from &&
                        x.CheckedAtUtc <= to)
            .GroupBy(x => x.ConnectionId)
            .Select(group => new HealthAggregate(group.Key, group.Count(),
                group.Count(x => x.Succeeded),
                group.Where(x => x.Succeeded).Max(x => (DateTimeOffset?)x.CheckedAtUtc)))
            .ToArrayAsync(cancellationToken);

        Dictionary<Guid, EventAggregate> inboundByConnection = inbound.ToDictionary(x => x.ConnectionId);
        Dictionary<Guid, EventAggregate> outboundByConnection = outbound.ToDictionary(x => x.ConnectionId);
        Dictionary<Guid, HealthAggregate> healthByConnection = health.ToDictionary(x => x.ConnectionId);
        IntegrationConnectionMetric[] connectionMetrics = connections.Select(connection =>
        {
            EventAggregate incoming = inboundByConnection.GetValueOrDefault(connection.Id) ?? EventAggregate.Empty(connection.Id);
            EventAggregate outgoing = outboundByConnection.GetValueOrDefault(connection.Id) ?? EventAggregate.Empty(connection.Id);
            HealthAggregate checks = healthByConnection.GetValueOrDefault(connection.Id) ?? HealthAggregate.Empty(connection.Id);
            return MapConnection(connection, incoming, outgoing, checks);
        }).ToArray();

        DailyMetric[] daily = await BuildDailyMetricsAsync(connectionIds, from, to, cancellationToken);
        ProviderMetric[] providers = connectionMetrics.GroupBy(x => x.Provider)
            .Select(group => new ProviderMetric(group.Key, group.Count(), group.Count(x => x.IsActive),
                group.Sum(x => x.InboundTotal), group.Sum(x => x.InboundCompleted),
                group.Sum(x => x.OutboundTotal), group.Sum(x => x.OutboundCompleted),
                group.Sum(x => x.RetryAttempts), group.Sum(x => x.DeadLetters),
                Percentage(group.Sum(x => x.InboundCompleted + x.OutboundCompleted),
                    group.Sum(x => x.InboundTotal + x.OutboundTotal)),
                Percentage(group.Sum(x => x.SuccessfulHealthChecks), group.Sum(x => x.HealthChecks))))
            .OrderBy(x => x.Provider).ToArray();

        int totalEvents = connectionMetrics.Sum(x => x.InboundTotal + x.OutboundTotal);
        int completedEvents = connectionMetrics.Sum(x => x.InboundCompleted + x.OutboundCompleted);
        DateTimeOffset? lastSuccessfulCommunication = connectionMetrics
            .Select(x => x.LastSuccessfulCommunicationAtUtc).Where(x => x.HasValue)
            .DefaultIfEmpty().Max();
        return Ok(new IntegrationMonitoringResponse(from, to, days, connections.Length,
            connections.Count(x => x.IsActive),
            connections.Count(x => x.IsActive && x.LastHealthCheckSucceeded == false),
            connectionMetrics.Sum(x => x.InboundTotal), connectionMetrics.Sum(x => x.InboundCompleted),
            connectionMetrics.Sum(x => x.OutboundTotal), connectionMetrics.Sum(x => x.OutboundCompleted),
            connectionMetrics.Sum(x => x.RetryAttempts), connectionMetrics.Sum(x => x.DeadLetters),
            Percentage(completedEvents, totalEvents),
            Percentage(connectionMetrics.Sum(x => x.SuccessfulHealthChecks),
                connectionMetrics.Sum(x => x.HealthChecks)),
            WeightedAverage(connectionMetrics), lastSuccessfulCommunication,
            providers, connectionMetrics, daily));
    }

    private async Task<DailyMetric[]> BuildDailyMetricsAsync(Guid[] connectionIds, DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var inbound = await context.InboundOrderEvents.AsNoTracking()
            .Where(x => connectionIds.Contains(x.ConnectionId) && x.ReceivedAtUtc >= from && x.ReceivedAtUtc <= to)
            .GroupBy(x => x.ReceivedAtUtc.Date)
            .Select(group => new { Date = group.Key, Total = group.Count(),
                Completed = group.Count(x => x.Status == InboundEventStatus.Completed) })
            .ToArrayAsync(cancellationToken);
        var outbound = await context.OutboundOrderEvents.AsNoTracking()
            .Where(x => connectionIds.Contains(x.ConnectionId) && x.CreatedAtUtc >= from && x.CreatedAtUtc <= to)
            .GroupBy(x => x.CreatedAtUtc.Date)
            .Select(group => new { Date = group.Key, Total = group.Count(),
                Completed = group.Count(x => x.Status == OutboundEventStatus.Completed) })
            .ToArrayAsync(cancellationToken);
        Dictionary<DateTime, (int Total, int Completed)> outgoing = outbound
            .ToDictionary(x => x.Date, x => (x.Total, x.Completed));
        Dictionary<DateTime, (int Total, int Completed)> incoming = inbound
            .ToDictionary(x => x.Date, x => (x.Total, x.Completed));
        DateTime firstDay = from.UtcDateTime.Date;
        DateTime lastDay = to.UtcDateTime.Date;
        return Enumerable.Range(0, (lastDay - firstDay).Days + 1).Select(offset => firstDay.AddDays(offset))
            .Select(date => new DailyMetric(date, incoming.GetValueOrDefault(date).Total,
                incoming.GetValueOrDefault(date).Completed, outgoing.GetValueOrDefault(date).Total,
                outgoing.GetValueOrDefault(date).Completed)).TakeLast(Math.Max(1, (int)(to - from).TotalDays))
            .ToArray();
    }

    private static IntegrationConnectionMetric MapConnection(ConnectionInfo connection,
        EventAggregate inbound, EventAggregate outbound, HealthAggregate health)
    {
        int total = inbound.Total + outbound.Total;
        int completed = inbound.Completed + outbound.Completed;
        int durationCount = (inbound.AverageDurationMs.HasValue ? inbound.Completed : 0) +
                            (outbound.AverageDurationMs.HasValue ? outbound.Completed : 0);
        double? averageDuration = durationCount == 0 ? null :
            ((inbound.AverageDurationMs ?? 0) * inbound.Completed +
             (outbound.AverageDurationMs ?? 0) * outbound.Completed) / durationCount;
        DateTimeOffset? lastSuccess = new[] { inbound.LastSuccessAtUtc, outbound.LastSuccessAtUtc,
            health.LastSuccessAtUtc }.Where(x => x.HasValue).DefaultIfEmpty().Max();
        return new IntegrationConnectionMetric(connection.Id, connection.BusinessId, connection.Name,
            connection.Provider, connection.IsActive, connection.LastHealthCheckSucceeded,
            connection.LastHealthCheckAtUtc, inbound.Total, inbound.Completed, outbound.Total,
            outbound.Completed, inbound.RetryAttempts + outbound.RetryAttempts,
            inbound.DeadLetters + outbound.DeadLetters, Percentage(completed, total),
            health.Total, health.Successful, Percentage(health.Successful, health.Total),
            averageDuration, lastSuccess);
    }

    private static double? WeightedAverage(IEnumerable<IntegrationConnectionMetric> rows)
    {
        IntegrationConnectionMetric[] items = rows.Where(x => x.AverageProcessingMs.HasValue).ToArray();
        int weight = items.Sum(x => x.InboundCompleted + x.OutboundCompleted);
        return weight == 0 ? null : items.Sum(x => x.AverageProcessingMs!.Value *
            (x.InboundCompleted + x.OutboundCompleted)) / weight;
    }

    private static decimal? Percentage(int value, int total) => total == 0
        ? null
        : Math.Round(value * 100m / total, 2);

    private sealed record ConnectionInfo(Guid Id, Guid BusinessId, string Name,
        IntegrationProvider Provider, bool IsActive, bool? LastHealthCheckSucceeded,
        DateTimeOffset? LastHealthCheckAtUtc);
    private sealed record EventAggregate(Guid ConnectionId, int Total, int Completed, int Failed,
        int DeadLetters, int RetryAttempts, double? AverageDurationMs, DateTimeOffset? LastSuccessAtUtc)
    {
        public static EventAggregate Empty(Guid id) => new(id, 0, 0, 0, 0, 0, null, null);
    }
    private sealed record HealthAggregate(Guid ConnectionId, int Total, int Successful,
        DateTimeOffset? LastSuccessAtUtc)
    {
        public static HealthAggregate Empty(Guid id) => new(id, 0, 0, null);
    }
}

public sealed record IntegrationMonitoringResponse(DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Days,
    int ConnectionCount, int ActiveConnectionCount, int UnhealthyConnectionCount,
    int InboundTotal, int InboundCompleted, int OutboundTotal, int OutboundCompleted,
    int RetryAttempts, int DeadLetters, decimal? SuccessRatePercent, decimal? AvailabilityPercent,
    double? AverageProcessingMs, DateTimeOffset? LastSuccessfulCommunicationAtUtc,
    IReadOnlyList<ProviderMetric> Providers, IReadOnlyList<IntegrationConnectionMetric> Connections,
    IReadOnlyList<DailyMetric> Daily);
public sealed record ProviderMetric(IntegrationProvider Provider, int ConnectionCount,
    int ActiveConnectionCount, int InboundTotal, int InboundCompleted, int OutboundTotal,
    int OutboundCompleted, int RetryAttempts, int DeadLetters, decimal? SuccessRatePercent,
    decimal? AvailabilityPercent);
public sealed record IntegrationConnectionMetric(Guid ConnectionId, Guid BusinessId, string Name,
    IntegrationProvider Provider, bool IsActive, bool? LastHealthCheckSucceeded,
    DateTimeOffset? LastHealthCheckAtUtc, int InboundTotal, int InboundCompleted,
    int OutboundTotal, int OutboundCompleted, int RetryAttempts, int DeadLetters,
    decimal? SuccessRatePercent, int HealthChecks, int SuccessfulHealthChecks,
    decimal? AvailabilityPercent, double? AverageProcessingMs,
    DateTimeOffset? LastSuccessfulCommunicationAtUtc);
public sealed record DailyMetric(DateTime Date, int InboundTotal, int InboundCompleted,
    int OutboundTotal, int OutboundCompleted);

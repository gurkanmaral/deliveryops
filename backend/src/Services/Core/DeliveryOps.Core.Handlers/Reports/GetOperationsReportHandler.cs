using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Reports;

public sealed class GetOperationsReportHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetOperationsReportQuery, Result<OperationsReportResponse>>
{
    private const string ReportTimeZoneId = "Europe/Istanbul";

    public async Task<Result<OperationsReportResponse>> Handle(
        GetOperationsReportQuery request,
        CancellationToken cancellationToken)
    {
        if (request.To < request.From)
            return Result<OperationsReportResponse>.Failure(HandlerErrors.Validation("Bitiş tarihi başlangıç tarihinden önce olamaz."));
        if (request.To.DayNumber - request.From.DayNumber > 366)
            return Result<OperationsReportResponse>.Failure(HandlerErrors.Validation("Rapor aralığı en fazla 367 gün olabilir."));

        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!requestContext.IsPlatformAdmin && !businessId.HasValue)
            return Result<OperationsReportResponse>.Failure(HandlerErrors.Forbidden);
        if (businessId.HasValue && !TenantAccess.CanAccess(requestContext, businessId.Value))
            return Result<OperationsReportResponse>.Failure(HandlerErrors.Forbidden);

        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(ReportTimeZoneId);
        DateTimeOffset fromUtc = ToUtc(request.From, timeZone);
        DateTimeOffset toExclusiveUtc = ToUtc(request.To.AddDays(1), timeZone);

        IQueryable<Order> query = context.Orders.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toExclusiveUtc);
        if (businessId.HasValue) query = query.Where(x => x.BusinessId == businessId.Value);
        if (requestContext.BranchId.HasValue)
            query = query.Where(x => x.BranchId == requestContext.BranchId.Value);

        List<OrderSnapshot> orders = await query.Select(x => new OrderSnapshot(
            x.Id, x.BusinessId, x.BranchId, x.CourierId, x.Source, x.Status,
            x.TotalAmount, x.CreatedAtUtc)).ToListAsync(cancellationToken);

        List<Guid> orderIds = orders.Select(x => x.Id).ToList();
        List<StatusSnapshot> history = orderIds.Count == 0
            ? []
            : await context.OrderStatusHistory.AsNoTracking()
                .Where(x => orderIds.Contains(x.OrderId))
                .Select(x => new StatusSnapshot(x.OrderId, x.Status, x.CreatedAtUtc))
                .ToListAsync(cancellationToken);

        Dictionary<Guid, IReadOnlyDictionary<OrderStatus, DateTimeOffset>> transitions = history
            .GroupBy(x => x.OrderId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<OrderStatus, DateTimeOffset>)group
                    .GroupBy(x => x.Status)
                    .ToDictionary(items => items.Key, items => items.Min(x => x.CreatedAtUtc)));

        Dictionary<Guid, string> courierNames = await LoadCourierNames(orders, cancellationToken);
        Dictionary<Guid, BranchLabel> branchLabels = await LoadBranchLabels(orders, cancellationToken);
        OperationsReportResponse report = BuildReport(request, businessId, orders, transitions,
            courierNames, branchLabels, timeZone);
        return Result<OperationsReportResponse>.Success(report);
    }

    private async Task<Dictionary<Guid, string>> LoadCourierNames(
        IReadOnlyCollection<OrderSnapshot> orders,
        CancellationToken cancellationToken)
    {
        List<Guid> ids = orders.Where(x => x.CourierId.HasValue).Select(x => x.CourierId!.Value).Distinct().ToList();
        return ids.Count == 0
            ? []
            : await context.Couriers.AsNoTracking().Where(x => ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName, cancellationToken);
    }

    private async Task<Dictionary<Guid, BranchLabel>> LoadBranchLabels(
        IReadOnlyCollection<OrderSnapshot> orders,
        CancellationToken cancellationToken)
    {
        List<Guid> ids = orders.Select(x => x.BranchId).Distinct().ToList();
        return ids.Count == 0
            ? []
            : await context.Branches.AsNoTracking().Where(x => ids.Contains(x.Id))
                .Select(x => new { x.Id, x.BusinessId, x.Name, BusinessName = x.Business.Name })
                .ToDictionaryAsync(x => x.Id,
                    x => new BranchLabel(x.BusinessId, x.BusinessName, x.Name), cancellationToken);
    }

    private static OperationsReportResponse BuildReport(
        GetOperationsReportQuery request,
        Guid? businessId,
        IReadOnlyCollection<OrderSnapshot> orders,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<OrderStatus, DateTimeOffset>> transitions,
        IReadOnlyDictionary<Guid, string> courierNames,
        IReadOnlyDictionary<Guid, BranchLabel> branchLabels,
        TimeZoneInfo timeZone)
    {
        OperationsReportSummary summary = BuildSummary(orders, transitions);
        List<DailyOrderMetric> daily = [];
        for (DateOnly date = request.From; date <= request.To; date = date.AddDays(1))
        {
            DateOnly current = date;
            List<OrderSnapshot> dayOrders = orders.Where(x => DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(x.CreatedAtUtc, timeZone).DateTime) == current).ToList();
            daily.Add(new DailyOrderMetric(current, dayOrders.Count,
                dayOrders.Count(IsDelivered), dayOrders.Count(IsCancelled), dayOrders.Count(IsFailed),
                dayOrders.Count(IsOpen), dayOrders.Sum(x => x.TotalAmount),
                dayOrders.Where(IsDelivered).Sum(x => x.TotalAmount)));
        }

        List<CourierPerformanceMetric> couriers = orders.Where(x => x.CourierId.HasValue)
            .GroupBy(x => x.CourierId!.Value)
            .Select(group => new CourierPerformanceMetric(group.Key,
                courierNames.GetValueOrDefault(group.Key, "Bilinmeyen kurye"), group.Count(),
                group.Count(IsDelivered), group.Count(IsFailed), group.Count(IsOpen), SuccessRate(group),
                AverageMinutes(group, transitions, OrderStatus.PickedUp, OrderStatus.Delivered),
                AverageTotalMinutes(group, transitions)))
            .OrderByDescending(x => x.DeliveredOrders).ThenBy(x => x.CourierName).ToList();

        List<BranchPerformanceMetric> branches = orders.GroupBy(x => x.BranchId)
            .Select(group =>
            {
                BranchLabel label = branchLabels.GetValueOrDefault(group.Key,
                    new BranchLabel(group.First().BusinessId, "Bilinmeyen işletme", "Bilinmeyen şube"));
                return new BranchPerformanceMetric(group.Key, label.BusinessId, label.BusinessName,
                    label.BranchName, group.Count(), group.Count(IsDelivered), group.Count(IsCancelled),
                    group.Count(IsFailed), group.Count(IsOpen), SuccessRate(group),
                    AverageTotalMinutes(group, transitions), group.Sum(x => x.TotalAmount),
                    group.Where(IsDelivered).Sum(x => x.TotalAmount));
            })
            .OrderByDescending(x => x.TotalOrders).ThenBy(x => x.BranchName).ToList();

        List<SourcePerformanceMetric> sources = orders.GroupBy(x => x.Source)
            .Select(group => new SourcePerformanceMetric(group.Key, group.Count(), group.Count(IsDelivered),
                group.Count(IsFailed), SuccessRate(group), group.Sum(x => x.TotalAmount)))
            .OrderByDescending(x => x.TotalOrders).ToList();

        return new OperationsReportResponse(request.From, request.To, ReportTimeZoneId, businessId,
            summary, daily, couriers, branches, sources);
    }

    private static OperationsReportSummary BuildSummary(
        IReadOnlyCollection<OrderSnapshot> orders,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<OrderStatus, DateTimeOffset>> transitions) =>
        new(orders.Count, orders.Count(IsDelivered), orders.Count(IsCancelled), orders.Count(IsFailed),
            orders.Count(IsOpen), orders.Sum(x => x.TotalAmount),
            orders.Where(IsDelivered).Sum(x => x.TotalAmount), SuccessRate(orders),
            AverageFromCreated(orders, transitions, OrderStatus.Assigned),
            AverageMinutes(orders, transitions, OrderStatus.Assigned, OrderStatus.PickedUp),
            AverageMinutes(orders, transitions, OrderStatus.PickedUp, OrderStatus.Delivered),
            AverageTotalMinutes(orders, transitions));

    private static bool IsDelivered(OrderSnapshot order) => order.Status == OrderStatus.Delivered;
    private static bool IsCancelled(OrderSnapshot order) => order.Status == OrderStatus.Cancelled;
    private static bool IsFailed(OrderSnapshot order) => order.Status is OrderStatus.DeliveryFailed or OrderStatus.Returned;
    private static bool IsOpen(OrderSnapshot order) => order.Status is not (OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Returned);

    private static double SuccessRate(IEnumerable<OrderSnapshot> source)
    {
        List<OrderSnapshot> terminal = source.Where(x => x.Status is OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Returned).ToList();
        return terminal.Count == 0 ? 0 : Math.Round((double)terminal.Count(IsDelivered) / terminal.Count, 4);
    }

    private static double? AverageFromCreated(IEnumerable<OrderSnapshot> orders,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<OrderStatus, DateTimeOffset>> transitions,
        OrderStatus target)
    {
        List<double> values = orders.Select(order => TryGetTransition(transitions, order.Id, target, out DateTimeOffset at)
                ? (double?)(at - order.CreatedAtUtc).TotalMinutes : null)
            .Where(x => x.HasValue && x.Value >= 0).Select(x => x!.Value).ToList();
        return values.Count == 0 ? null : Math.Round(values.Average(), 1);
    }

    private static double? AverageTotalMinutes(IEnumerable<OrderSnapshot> orders,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<OrderStatus, DateTimeOffset>> transitions) =>
        AverageFromCreated(orders, transitions, OrderStatus.Delivered);

    private static double? AverageMinutes(IEnumerable<OrderSnapshot> orders,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<OrderStatus, DateTimeOffset>> transitions,
        OrderStatus from,
        OrderStatus to)
    {
        List<double> values = [];
        foreach (OrderSnapshot order in orders)
        {
            if (TryGetTransition(transitions, order.Id, from, out DateTimeOffset started) &&
                TryGetTransition(transitions, order.Id, to, out DateTimeOffset completed) && completed >= started)
                values.Add((completed - started).TotalMinutes);
        }
        return values.Count == 0 ? null : Math.Round(values.Average(), 1);
    }

    private static bool TryGetTransition(
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<OrderStatus, DateTimeOffset>> transitions,
        Guid orderId,
        OrderStatus status,
        out DateTimeOffset at)
    {
        if (transitions.TryGetValue(orderId, out IReadOnlyDictionary<OrderStatus, DateTimeOffset>? statuses) &&
            statuses.TryGetValue(status, out at)) return true;
        at = default;
        return false;
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        DateTime local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
    }

    private sealed record OrderSnapshot(Guid Id, Guid BusinessId, Guid BranchId, Guid? CourierId,
        OrderSource Source, OrderStatus Status, decimal TotalAmount, DateTimeOffset CreatedAtUtc);
    private sealed record StatusSnapshot(Guid OrderId, OrderStatus Status, DateTimeOffset CreatedAtUtc);
    private sealed record BranchLabel(Guid BusinessId, string BusinessName, string BranchName);
}

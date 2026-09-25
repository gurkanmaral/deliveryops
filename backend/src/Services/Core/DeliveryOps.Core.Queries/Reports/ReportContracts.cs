using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Enums;
using MediatR;

namespace DeliveryOps.Core.Queries.Reports;

public sealed record OperationsReportSummary(
    int TotalOrders,
    int DeliveredOrders,
    int CancelledOrders,
    int FailedOrders,
    int OpenOrders,
    decimal TotalOrderValue,
    decimal DeliveredValue,
    double DeliverySuccessRate,
    double? AverageAssignmentMinutes,
    double? AveragePickupMinutes,
    double? AverageDeliveryMinutes,
    double? AverageTotalMinutes);

public sealed record DailyOrderMetric(
    DateOnly Date,
    int TotalOrders,
    int DeliveredOrders,
    int CancelledOrders,
    int FailedOrders,
    int OpenOrders,
    decimal TotalOrderValue,
    decimal DeliveredValue);

public sealed record CourierPerformanceMetric(
    Guid CourierId,
    string CourierName,
    int AssignedOrders,
    int DeliveredOrders,
    int FailedOrders,
    int OpenOrders,
    double DeliverySuccessRate,
    double? AverageDeliveryMinutes,
    double? AverageTotalMinutes);

public sealed record BranchPerformanceMetric(
    Guid BranchId,
    Guid BusinessId,
    string BusinessName,
    string BranchName,
    int TotalOrders,
    int DeliveredOrders,
    int CancelledOrders,
    int FailedOrders,
    int OpenOrders,
    double DeliverySuccessRate,
    double? AverageTotalMinutes,
    decimal TotalOrderValue,
    decimal DeliveredValue);

public sealed record SourcePerformanceMetric(
    OrderSource Source,
    int TotalOrders,
    int DeliveredOrders,
    int FailedOrders,
    double DeliverySuccessRate,
    decimal TotalOrderValue);

public sealed record OperationsReportResponse(
    DateOnly From,
    DateOnly To,
    string TimeZone,
    Guid? BusinessId,
    OperationsReportSummary Summary,
    IReadOnlyList<DailyOrderMetric> Daily,
    IReadOnlyList<CourierPerformanceMetric> Couriers,
    IReadOnlyList<BranchPerformanceMetric> Branches,
    IReadOnlyList<SourcePerformanceMetric> Sources);

public sealed record GetOperationsReportQuery(DateOnly From, DateOnly To, Guid? BusinessId)
    : IRequest<Result<OperationsReportResponse>>;

using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Enums;
using MediatR;

namespace DeliveryOps.Core.Queries.Billing;

public sealed record BillingSettingsResponse(Guid BusinessId, decimal FeePerDeliveredOrder,
    decimal CommissionRatePercent, decimal FeePerReturnedOrder, decimal TaxRatePercent, string Currency);

public sealed record BillingPreviewResponse(Guid BusinessId, string BusinessName, DateOnly PeriodFrom,
    DateOnly PeriodTo, int DeliveredOrderCount, int ReturnedOrderCount, int CancelledOrderCount,
    decimal DeliveredOrderValue, decimal FeePerDeliveredOrder, decimal CommissionRatePercent,
    decimal FeePerReturnedOrder, decimal TaxRatePercent, decimal DeliveryFeeAmount,
    decimal CommissionAmount, decimal ReturnFeeAmount, decimal SubtotalAmount,
    decimal TaxAmount, decimal TotalAmount, string Currency);

public sealed record BillingSettlementResponse(Guid Id, Guid BusinessId, string BusinessName,
    DateOnly PeriodFrom, DateOnly PeriodTo, BillingSettlementStatus Status,
    int DeliveredOrderCount, int ReturnedOrderCount, int CancelledOrderCount,
    decimal DeliveredOrderValue, decimal FeePerDeliveredOrder, decimal CommissionRatePercent,
    decimal FeePerReturnedOrder, decimal TaxRatePercent, decimal DeliveryFeeAmount,
    decimal CommissionAmount, decimal ReturnFeeAmount, decimal SubtotalAmount,
    decimal TaxAmount, decimal TotalAmount, string Currency, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? FinalizedAtUtc, string DocumentNumber);

public sealed record GetBillingSettingsQuery(Guid? BusinessId) : IRequest<Result<BillingSettingsResponse>>;
public sealed record UpdateBillingSettingsCommand(Guid? BusinessId, decimal FeePerDeliveredOrder,
    decimal CommissionRatePercent, decimal FeePerReturnedOrder, decimal TaxRatePercent)
    : IRequest<Result<BillingSettingsResponse>>;
public sealed record GetBillingPreviewQuery(Guid? BusinessId, DateOnly From, DateOnly To)
    : IRequest<Result<BillingPreviewResponse>>;
public sealed record GetBillingSettlementsQuery(Guid? BusinessId, int Page = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<BillingSettlementResponse>>>;
public sealed record CreateBillingSettlementCommand(Guid? BusinessId, DateOnly From, DateOnly To)
    : IRequest<Result<BillingSettlementResponse>>;
public sealed record FinalizeBillingSettlementCommand(Guid Id)
    : IRequest<Result<BillingSettlementResponse>>;
public sealed record GetBillingSettlementQuery(Guid Id)
    : IRequest<Result<BillingSettlementResponse>>;

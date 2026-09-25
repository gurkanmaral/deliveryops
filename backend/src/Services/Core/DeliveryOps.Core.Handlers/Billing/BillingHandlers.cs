using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Billing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Billing;

public sealed class GetBillingSettingsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetBillingSettingsQuery, Result<BillingSettingsResponse>>
{
    public async Task<Result<BillingSettingsResponse>> Handle(GetBillingSettingsQuery request, CancellationToken cancellationToken)
    {
        Result<Guid> business = await BillingAccess.ResolveBusiness(context, requestContext, request.BusinessId, cancellationToken);
        if (business.IsFailure) return Result<BillingSettingsResponse>.Failure(business.Error);
        BusinessBillingSettings settings = await context.BusinessBillingSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == business.Value, cancellationToken)
            ?? BusinessBillingSettings.CreateDefault(business.Value);
        return Result<BillingSettingsResponse>.Success(BillingMapper.Map(settings));
    }
}

public sealed class UpdateBillingSettingsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<UpdateBillingSettingsCommand, Result<BillingSettingsResponse>>
{
    public async Task<Result<BillingSettingsResponse>> Handle(UpdateBillingSettingsCommand request, CancellationToken cancellationToken)
    {
        Result<Guid> business = await BillingAccess.ResolveBusiness(context, requestContext, request.BusinessId, cancellationToken);
        if (business.IsFailure) return Result<BillingSettingsResponse>.Failure(business.Error);
        BusinessBillingSettings? settings = await context.BusinessBillingSettings
            .SingleOrDefaultAsync(x => x.BusinessId == business.Value, cancellationToken);
        if (settings is null)
        {
            settings = BusinessBillingSettings.CreateDefault(business.Value);
            context.BusinessBillingSettings.Add(settings);
        }
        try
        {
            settings.Update(request.FeePerDeliveredOrder, request.CommissionRatePercent,
                request.FeePerReturnedOrder, request.TaxRatePercent);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result<BillingSettingsResponse>.Failure(HandlerErrors.Validation(
                "Ücretler negatif olamaz; komisyon ve vergi oranı 0-100 arasında olmalıdır."));
        }
        await context.SaveChangesAsync(cancellationToken);
        return Result<BillingSettingsResponse>.Success(BillingMapper.Map(settings));
    }
}

public sealed class GetBillingPreviewHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetBillingPreviewQuery, Result<BillingPreviewResponse>>
{
    public async Task<Result<BillingPreviewResponse>> Handle(GetBillingPreviewQuery request, CancellationToken cancellationToken)
    {
        Result<Guid> business = await BillingAccess.ResolveBusiness(context, requestContext, request.BusinessId, cancellationToken);
        if (business.IsFailure) return Result<BillingPreviewResponse>.Failure(business.Error);
        Error? dateError = BillingCalculator.ValidatePeriod(request.From, request.To);
        if (dateError is not null) return Result<BillingPreviewResponse>.Failure(dateError);
        string businessName = await context.Businesses.Where(x => x.Id == business.Value)
            .Select(x => x.Name).SingleAsync(cancellationToken);
        BusinessBillingSettings settings = await context.BusinessBillingSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == business.Value, cancellationToken)
            ?? BusinessBillingSettings.CreateDefault(business.Value);
        BillingTotals totals = await BillingCalculator.Calculate(context, business.Value,
            request.From, request.To, settings, cancellationToken);
        return Result<BillingPreviewResponse>.Success(BillingMapper.MapPreview(business.Value,
            businessName, request.From, request.To, settings, totals));
    }
}

public sealed class GetBillingSettlementsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetBillingSettlementsQuery, Result<PagedResponse<BillingSettlementResponse>>>
{
    public async Task<Result<PagedResponse<BillingSettlementResponse>>> Handle(GetBillingSettlementsQuery request, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, request.BusinessId);
        if (!requestContext.IsPlatformAdmin && !businessId.HasValue)
            return Result<PagedResponse<BillingSettlementResponse>>.Failure(HandlerErrors.Forbidden);
        IQueryable<BillingSettlement> query = context.BillingSettlements.AsNoTracking();
        if (businessId.HasValue) query = query.Where(x => x.BusinessId == businessId.Value);
        PagedResponse<SettlementProjection> page = await query.OrderByDescending(x => x.PeriodTo)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Join(context.Businesses.AsNoTracking(), settlement => settlement.BusinessId, business => business.Id,
                (settlement, business) => new SettlementProjection(settlement, business.Name))
            .ToPagedAsync(request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<BillingSettlementResponse>>.Success(new(
            page.Items.Select(x => BillingMapper.Map(x.Settlement, x.BusinessName)).ToArray(),
            page.Page, page.PageSize, page.TotalCount));
    }

    private sealed record SettlementProjection(BillingSettlement Settlement, string BusinessName);
}

public sealed class CreateBillingSettlementHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<CreateBillingSettlementCommand, Result<BillingSettlementResponse>>
{
    public async Task<Result<BillingSettlementResponse>> Handle(CreateBillingSettlementCommand request, CancellationToken cancellationToken)
    {
        Result<Guid> business = await BillingAccess.ResolveBusiness(context, requestContext, request.BusinessId, cancellationToken);
        if (business.IsFailure) return Result<BillingSettlementResponse>.Failure(business.Error);
        Error? dateError = BillingCalculator.ValidatePeriod(request.From, request.To);
        if (dateError is not null) return Result<BillingSettlementResponse>.Failure(dateError);
        string businessName = await context.Businesses.Where(x => x.Id == business.Value)
            .Select(x => x.Name).SingleAsync(cancellationToken);
        BusinessBillingSettings settings = await context.BusinessBillingSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == business.Value, cancellationToken)
            ?? BusinessBillingSettings.CreateDefault(business.Value);
        BillingTotals totals = await BillingCalculator.Calculate(context, business.Value,
            request.From, request.To, settings, cancellationToken);
        BillingSettlement? settlement = await context.BillingSettlements.SingleOrDefaultAsync(x =>
            x.BusinessId == business.Value && x.PeriodFrom == request.From && x.PeriodTo == request.To,
            cancellationToken);
        bool overlapsAnotherPeriod = await context.BillingSettlements.AnyAsync(x =>
            x.BusinessId == business.Value && (settlement == null || x.Id != settlement.Id) &&
            x.PeriodFrom <= request.To && x.PeriodTo >= request.From, cancellationToken);
        if (overlapsAnotherPeriod)
            return Result<BillingSettlementResponse>.Failure(HandlerErrors.Conflict(
                "Bu tarih aralığı mevcut başka bir mutabakat dönemiyle çakışıyor."));
        try
        {
            if (settlement is null)
            {
                settlement = BillingSettlement.Create(business.Value, request.From, request.To,
                    totals.DeliveredCount, totals.ReturnedCount, totals.CancelledCount,
                    totals.DeliveredValue, settings, requestContext.UserId);
                context.BillingSettlements.Add(settlement);
            }
            else
            {
                settlement.Recalculate(totals.DeliveredCount, totals.ReturnedCount,
                    totals.CancelledCount, totals.DeliveredValue, settings);
            }
        }
        catch (InvalidOperationException exception)
        {
            return Result<BillingSettlementResponse>.Failure(HandlerErrors.Conflict(exception.Message));
        }
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result<BillingSettlementResponse>.Failure(HandlerErrors.Conflict(
                "Aynı dönem için başka bir mutabakat kaydı oluşturuldu. Listeyi yenileyin."));
        }
        return Result<BillingSettlementResponse>.Success(BillingMapper.Map(settlement, businessName));
    }
}

public sealed class FinalizeBillingSettlementHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<FinalizeBillingSettlementCommand, Result<BillingSettlementResponse>>
{
    public async Task<Result<BillingSettlementResponse>> Handle(FinalizeBillingSettlementCommand request, CancellationToken cancellationToken)
    {
        BillingSettlement? settlement = await context.BillingSettlements.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (settlement is null) return Result<BillingSettlementResponse>.Failure(HandlerErrors.NotFound("Mutabakat"));
        if (!TenantAccess.CanAccess(requestContext, settlement.BusinessId))
            return Result<BillingSettlementResponse>.Failure(HandlerErrors.Forbidden);
        settlement.Finalize(requestContext.UserId, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(cancellationToken);
        string businessName = await context.Businesses.Where(x => x.Id == settlement.BusinessId)
            .Select(x => x.Name).SingleAsync(cancellationToken);
        return Result<BillingSettlementResponse>.Success(BillingMapper.Map(settlement, businessName));
    }
}

public sealed class GetBillingSettlementHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetBillingSettlementQuery, Result<BillingSettlementResponse>>
{
    public async Task<Result<BillingSettlementResponse>> Handle(GetBillingSettlementQuery request,
        CancellationToken cancellationToken)
    {
        SettlementWithBusiness? item = await context.BillingSettlements.AsNoTracking()
            .Where(x => x.Id == request.Id)
            .Join(context.Businesses.AsNoTracking(), settlement => settlement.BusinessId,
                business => business.Id, (settlement, business) =>
                    new SettlementWithBusiness(settlement, business.Name))
            .SingleOrDefaultAsync(cancellationToken);
        if (item is null) return Result<BillingSettlementResponse>.Failure(HandlerErrors.NotFound("Mutabakat"));
        if (!TenantAccess.CanAccess(requestContext, item.Settlement.BusinessId))
            return Result<BillingSettlementResponse>.Failure(HandlerErrors.Forbidden);
        return Result<BillingSettlementResponse>.Success(BillingMapper.Map(item.Settlement, item.BusinessName));
    }

    private sealed record SettlementWithBusiness(BillingSettlement Settlement, string BusinessName);
}

internal static class BillingAccess
{
    public static async Task<Result<Guid>> ResolveBusiness(ICoreDbContext context,
        IRequestContext requestContext, Guid? requestedBusinessId, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, requestedBusinessId);
        if (!businessId.HasValue) return Result<Guid>.Failure(HandlerErrors.Validation("İşletme seçilmelidir."));
        if (!TenantAccess.CanAccess(requestContext, businessId.Value)) return Result<Guid>.Failure(HandlerErrors.Forbidden);
        if (!await context.Businesses.AnyAsync(x => x.Id == businessId.Value && x.IsActive, cancellationToken))
            return Result<Guid>.Failure(HandlerErrors.NotFound("İşletme"));
        return Result<Guid>.Success(businessId.Value);
    }
}

internal sealed record BillingTotals(int DeliveredCount, int ReturnedCount, int CancelledCount,
    decimal DeliveredValue, decimal DeliveryFeeAmount, decimal CommissionAmount,
    decimal ReturnFeeAmount, decimal SubtotalAmount, decimal TaxAmount, decimal TotalAmount);

internal static class BillingCalculator
{
    private const string TimeZoneId = "Europe/Istanbul";

    public static Error? ValidatePeriod(DateOnly from, DateOnly to)
    {
        if (to < from) return HandlerErrors.Validation("Bitiş tarihi başlangıç tarihinden önce olamaz.");
        if (to.DayNumber - from.DayNumber > 366) return HandlerErrors.Validation("Mutabakat aralığı en fazla 367 gün olabilir.");
        return null;
    }

    public static async Task<BillingTotals> Calculate(ICoreDbContext context, Guid businessId,
        DateOnly from, DateOnly to, BusinessBillingSettings settings, CancellationToken cancellationToken)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        DateTimeOffset fromUtc = ToUtc(from, zone);
        DateTimeOffset toExclusiveUtc = ToUtc(to.AddDays(1), zone);
        List<Outcome> outcomes = await context.OrderStatusHistory.AsNoTracking()
            .Where(x => x.Order.BusinessId == businessId && x.CreatedAtUtc >= fromUtc &&
                x.CreatedAtUtc < toExclusiveUtc &&
                (x.Status == OrderStatus.Delivered || x.Status == OrderStatus.Returned || x.Status == OrderStatus.Cancelled))
            .Select(x => new Outcome(x.OrderId, x.Status, x.Order.TotalAmount))
            .ToListAsync(cancellationToken);
        List<Outcome> unique = outcomes.GroupBy(x => new { x.OrderId, x.Status }).Select(x => x.First()).ToList();
        int delivered = unique.Count(x => x.Status == OrderStatus.Delivered);
        int returned = unique.Count(x => x.Status == OrderStatus.Returned);
        int cancelled = unique.Count(x => x.Status == OrderStatus.Cancelled);
        decimal deliveredValue = unique.Where(x => x.Status == OrderStatus.Delivered).Sum(x => x.TotalAmount);
        decimal deliveryFees = decimal.Round(delivered * settings.FeePerDeliveredOrder, 2);
        decimal commission = decimal.Round(deliveredValue * settings.CommissionRatePercent / 100m, 2);
        decimal returnFees = decimal.Round(returned * settings.FeePerReturnedOrder, 2);
        decimal subtotal = deliveryFees + commission + returnFees;
        decimal tax = decimal.Round(subtotal * settings.TaxRatePercent / 100m, 2);
        return new BillingTotals(delivered, returned, cancelled, deliveredValue, deliveryFees,
            commission, returnFees, subtotal, tax, subtotal + tax);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeZoneInfo zone)
    {
        DateTime local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private sealed record Outcome(Guid OrderId, OrderStatus Status, decimal TotalAmount);
}

internal static class BillingMapper
{
    public static BillingSettingsResponse Map(BusinessBillingSettings settings) => new(settings.BusinessId,
        settings.FeePerDeliveredOrder, settings.CommissionRatePercent, settings.FeePerReturnedOrder,
        settings.TaxRatePercent, settings.Currency);

    public static BillingPreviewResponse MapPreview(Guid businessId, string businessName, DateOnly from,
        DateOnly to, BusinessBillingSettings settings, BillingTotals totals) => new(businessId,
        businessName, from, to, totals.DeliveredCount, totals.ReturnedCount, totals.CancelledCount,
        totals.DeliveredValue, settings.FeePerDeliveredOrder, settings.CommissionRatePercent,
        settings.FeePerReturnedOrder, settings.TaxRatePercent, totals.DeliveryFeeAmount,
        totals.CommissionAmount, totals.ReturnFeeAmount, totals.SubtotalAmount, totals.TaxAmount,
        totals.TotalAmount, settings.Currency);

    public static BillingSettlementResponse Map(BillingSettlement settlement, string businessName) =>
        new(settlement.Id, settlement.BusinessId, businessName, settlement.PeriodFrom,
            settlement.PeriodTo, settlement.Status, settlement.DeliveredOrderCount,
            settlement.ReturnedOrderCount, settlement.CancelledOrderCount,
            settlement.DeliveredOrderValue, settlement.FeePerDeliveredOrder,
            settlement.CommissionRatePercent, settlement.FeePerReturnedOrder,
            settlement.TaxRatePercent, settlement.DeliveryFeeAmount, settlement.CommissionAmount,
            settlement.ReturnFeeAmount, settlement.SubtotalAmount, settlement.TaxAmount,
            settlement.TotalAmount, settlement.Currency, settlement.CreatedAtUtc, settlement.FinalizedAtUtc,
            settlement.DocumentNumber);
}

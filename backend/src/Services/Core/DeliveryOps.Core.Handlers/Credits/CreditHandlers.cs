using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Common;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Credits;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Credits;

public sealed class GetCreditAccountHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetCreditAccountQuery, Result<CreditAccountResponse>>
{
    public async Task<Result<CreditAccountResponse>> Handle(GetCreditAccountQuery request, CancellationToken cancellationToken)
    {
        Result<BusinessLabel> business = await CreditAccess.ResolveBusiness(context, requestContext,
            request.BusinessId, cancellationToken);
        if (business.IsFailure) return Result<CreditAccountResponse>.Failure(business.Error);
        BusinessCreditAccount? account = await context.BusinessCreditAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == business.Value!.Id, cancellationToken);
        return Result<CreditAccountResponse>.Success(CreditMapper.Map(account, business.Value!));
    }
}

public sealed class GetCreditTransactionsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<GetCreditTransactionsQuery, Result<PagedResponse<CreditTransactionResponse>>>
{
    public async Task<Result<PagedResponse<CreditTransactionResponse>>> Handle(GetCreditTransactionsQuery request, CancellationToken cancellationToken)
    {
        Result<BusinessLabel> business = await CreditAccess.ResolveBusiness(context, requestContext,
            request.BusinessId, cancellationToken);
        if (business.IsFailure) return Result<PagedResponse<CreditTransactionResponse>>.Failure(business.Error);
        PagedResponse<CreditTransactionResponse> items = await context.CreditTransactions.AsNoTracking()
            .Where(x => x.BusinessId == business.Value!.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new CreditTransactionResponse(x.Id, x.BusinessId, x.Type, x.Amount,
                x.BalanceAfter, x.OrderId, x.Description, x.CreatedByUserId, x.IdempotencyKey, x.CreatedAtUtc))
            .ToPagedAsync(request.Page, request.PageSize, cancellationToken);
        return Result<PagedResponse<CreditTransactionResponse>>.Success(items);
    }
}

public sealed class GetCreditPackagesHandler : IRequestHandler<GetCreditPackagesQuery, PagedResponse<CreditPackageResponse>>
{
    public Task<PagedResponse<CreditPackageResponse>> Handle(GetCreditPackagesQuery request,
        CancellationToken cancellationToken) => Task.FromResult(
        Pagination.FromItems(CreditPackageCatalog.All, request.Page, request.PageSize));
}

public sealed class TopUpCreditsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<TopUpCreditsCommand, Result<CreditAccountResponse>>
{
    public async Task<Result<CreditAccountResponse>> Handle(TopUpCreditsCommand request, CancellationToken cancellationToken)
    {
        Result<BusinessLabel> business = await CreditAccess.ResolveBusiness(context, requestContext,
            request.BusinessId, cancellationToken);
        if (business.IsFailure) return Result<CreditAccountResponse>.Failure(business.Error);
        CreditPackageResponse? package = CreditPackageCatalog.Find(request.PackageCode);
        if (package is null)
            return Result<CreditAccountResponse>.Failure(HandlerErrors.Validation("Geçerli bir kredi paketi seçilmelidir."));
        Result? validation = CreditMutation.Validate(request.IdempotencyKey, request.Description, false);
        if (validation is not null) return Result<CreditAccountResponse>.Failure(validation.Error);
        string idempotencyKey = CreditMutation.Normalize(request.IdempotencyKey);
        CreditTransaction? existing = await CreditMutation.FindExisting(context, business.Value!.Id,
            idempotencyKey, cancellationToken);
        if (existing is not null)
            return await CreditMutation.Replay(context, business.Value!, existing,
                CreditTransactionType.TopUp, package.Amount, cancellationToken);
        BusinessCreditAccount? account = await context.BusinessCreditAccounts
            .SingleOrDefaultAsync(x => x.BusinessId == business.Value!.Id, cancellationToken);
        if (account is null)
        {
            account = BusinessCreditAccount.Create(business.Value!.Id);
            context.BusinessCreditAccounts.Add(account);
        }
        int balance = account.Add(package.Amount);
        string description = string.IsNullOrWhiteSpace(request.Description)
            ? $"{package.Name} yüklendi."
            : request.Description.Trim();
        context.CreditTransactions.Add(CreditTransaction.Create(account.BusinessId,
            CreditTransactionType.TopUp, package.Amount, balance, null, description,
            requestContext.UserId, idempotencyKey));
        Result? saveError = await CreditMutation.Save(context, cancellationToken);
        if (saveError is not null) return Result<CreditAccountResponse>.Failure(saveError.Error);
        return Result<CreditAccountResponse>.Success(CreditMapper.Map(account, business.Value!));
    }
}

public sealed class AdjustCreditsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<AdjustCreditsCommand, Result<CreditAccountResponse>>
{
    public async Task<Result<CreditAccountResponse>> Handle(AdjustCreditsCommand request, CancellationToken cancellationToken)
    {
        Result<BusinessLabel> business = await CreditAccess.ResolveBusiness(context, requestContext,
            request.BusinessId, cancellationToken);
        if (business.IsFailure) return Result<CreditAccountResponse>.Failure(business.Error);
        if (request.Amount is 0 or < -1_000_000 or > 1_000_000)
            return Result<CreditAccountResponse>.Failure(HandlerErrors.Validation("Düzeltme miktarı -1.000.000 ile 1.000.000 arasında ve sıfırdan farklı olmalıdır."));
        Result? validation = CreditMutation.Validate(request.IdempotencyKey, request.Description, true);
        if (validation is not null) return Result<CreditAccountResponse>.Failure(validation.Error);
        string idempotencyKey = CreditMutation.Normalize(request.IdempotencyKey);
        CreditTransaction? existing = await CreditMutation.FindExisting(context, business.Value!.Id,
            idempotencyKey, cancellationToken);
        if (existing is not null)
            return await CreditMutation.Replay(context, business.Value!, existing,
                CreditTransactionType.Adjustment, request.Amount, cancellationToken);
        BusinessCreditAccount? account = await context.BusinessCreditAccounts
            .SingleOrDefaultAsync(x => x.BusinessId == business.Value!.Id, cancellationToken);
        if (account is null) return Result<CreditAccountResponse>.Failure(HandlerErrors.Conflict("İşletmenin kredi hesabı bulunmuyor."));
        int balance;
        try { balance = account.Adjust(request.Amount); }
        catch (InvalidOperationException exception) { return Result<CreditAccountResponse>.Failure(HandlerErrors.Conflict(exception.Message)); }
        context.CreditTransactions.Add(CreditTransaction.Create(account.BusinessId,
            CreditTransactionType.Adjustment, request.Amount, balance, null, request.Description,
            requestContext.UserId, idempotencyKey));
        Result? saveError = await CreditMutation.Save(context, cancellationToken);
        if (saveError is not null) return Result<CreditAccountResponse>.Failure(saveError.Error);
        return Result<CreditAccountResponse>.Success(CreditMapper.Map(account, business.Value!));
    }
}

public sealed class RefundOrderCreditHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<RefundOrderCreditCommand, Result<CreditAccountResponse>>
{
    public async Task<Result<CreditAccountResponse>> Handle(RefundOrderCreditCommand request, CancellationToken cancellationToken)
    {
        Result<BusinessLabel> business = await CreditAccess.ResolveBusiness(context, requestContext,
            request.BusinessId, cancellationToken);
        if (business.IsFailure) return Result<CreditAccountResponse>.Failure(business.Error);
        Result? validation = CreditMutation.Validate(request.IdempotencyKey, request.Description, true);
        if (validation is not null) return Result<CreditAccountResponse>.Failure(validation.Error);
        string idempotencyKey = CreditMutation.Normalize(request.IdempotencyKey);
        CreditTransaction? replay = await CreditMutation.FindExisting(context, business.Value!.Id,
            idempotencyKey, cancellationToken);
        if (replay is not null)
            return await CreditMutation.Replay(context, business.Value!, replay,
                CreditTransactionType.Refund, 1, cancellationToken);
        CreditTransaction? consumption = await context.CreditTransactions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == business.Value!.Id && x.OrderId == request.OrderId &&
                x.Type == CreditTransactionType.OrderConsumption, cancellationToken);
        if (consumption is null)
            return Result<CreditAccountResponse>.Failure(HandlerErrors.NotFound("Sipariş kredi hareketi"));
        if (await context.CreditTransactions.AnyAsync(x => x.BusinessId == business.Value!.Id &&
            x.OrderId == request.OrderId && x.Type == CreditTransactionType.Refund, cancellationToken))
            return Result<CreditAccountResponse>.Failure(HandlerErrors.Conflict("Bu siparişin kredisi daha önce iade edildi."));
        BusinessCreditAccount? account = await context.BusinessCreditAccounts
            .SingleOrDefaultAsync(x => x.BusinessId == business.Value!.Id, cancellationToken);
        if (account is null) return Result<CreditAccountResponse>.Failure(HandlerErrors.Conflict("İşletmenin kredi hesabı bulunmuyor."));
        int amount = Math.Abs(consumption.Amount);
        int balance = account.Refund(amount);
        context.CreditTransactions.Add(CreditTransaction.Create(account.BusinessId,
            CreditTransactionType.Refund, amount, balance, request.OrderId, request.Description,
            requestContext.UserId, idempotencyKey));
        Result? saveError = await CreditMutation.Save(context, cancellationToken);
        if (saveError is not null) return Result<CreditAccountResponse>.Failure(saveError.Error);
        return Result<CreditAccountResponse>.Success(CreditMapper.Map(account, business.Value!));
    }
}

public sealed class UpdateCreditSettingsHandler(ICoreDbContext context, IRequestContext requestContext)
    : IRequestHandler<UpdateCreditSettingsCommand, Result<CreditAccountResponse>>
{
    public async Task<Result<CreditAccountResponse>> Handle(UpdateCreditSettingsCommand request,
        CancellationToken cancellationToken)
    {
        Result<BusinessLabel> business = await CreditAccess.ResolveBusiness(context, requestContext,
            request.BusinessId, cancellationToken);
        if (business.IsFailure) return Result<CreditAccountResponse>.Failure(business.Error);
        BusinessCreditAccount? account = await context.BusinessCreditAccounts
            .SingleOrDefaultAsync(x => x.BusinessId == business.Value!.Id, cancellationToken);
        if (account is null) return Result<CreditAccountResponse>.Failure(HandlerErrors.Conflict("İşletmenin kredi hesabı bulunmuyor."));
        try { account.UpdateLowBalanceThreshold(request.LowBalanceThreshold); }
        catch (ArgumentOutOfRangeException) { return Result<CreditAccountResponse>.Failure(HandlerErrors.Validation("Düşük bakiye eşiği 0 ile 1.000.000 arasında olmalıdır.")); }
        await context.SaveChangesAsync(cancellationToken);
        return Result<CreditAccountResponse>.Success(CreditMapper.Map(account, business.Value!));
    }
}

internal sealed record BusinessLabel(Guid Id, string Name);

internal static class CreditAccess
{
    public static async Task<Result<BusinessLabel>> ResolveBusiness(ICoreDbContext context,
        IRequestContext requestContext, Guid? requestedBusinessId, CancellationToken cancellationToken)
    {
        Guid? businessId = TenantAccess.ResolveBusinessId(requestContext, requestedBusinessId);
        if (!businessId.HasValue) return Result<BusinessLabel>.Failure(HandlerErrors.Validation("İşletme seçilmelidir."));
        if (!TenantAccess.CanAccess(requestContext, businessId.Value)) return Result<BusinessLabel>.Failure(HandlerErrors.Forbidden);
        BusinessLabel? business = await context.Businesses.AsNoTracking()
            .Where(x => x.Id == businessId.Value && x.IsActive)
            .Select(x => new BusinessLabel(x.Id, x.Name)).SingleOrDefaultAsync(cancellationToken);
        return business is null
            ? Result<BusinessLabel>.Failure(HandlerErrors.NotFound("İşletme"))
            : Result<BusinessLabel>.Success(business);
    }
}

internal static class CreditMapper
{
    public static CreditAccountResponse Map(BusinessCreditAccount? account, BusinessLabel business) =>
        new(business.Id, business.Name, account?.Balance ?? 0, account?.LifetimeAdded ?? 0,
            account?.LifetimeConsumed ?? 0, account?.LowBalanceThreshold ?? 100,
            (account?.Balance ?? 0) <= (account?.LowBalanceThreshold ?? 100), account?.UpdatedAtUtc);
}

internal static class CreditPackageCatalog
{
    public static readonly IReadOnlyList<CreditPackageResponse> All =
    [
        new("CREDIT_1000", "1.000 kredi paketi", 1_000),
        new("CREDIT_5000", "5.000 kredi paketi", 5_000),
        new("CREDIT_10000", "10.000 kredi paketi", 10_000)
    ];

    public static CreditPackageResponse? Find(string code) =>
        All.SingleOrDefault(x => string.Equals(x.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
}

internal static class CreditMutation
{
    public static string Normalize(string idempotencyKey) => Guid.Parse(idempotencyKey).ToString("D");

    public static Result? Validate(string idempotencyKey, string? description, bool descriptionRequired)
    {
        if (!Guid.TryParse(idempotencyKey, out _))
            return Result.Failure(HandlerErrors.Validation("Geçerli bir idempotency anahtarı gereklidir."));
        if (descriptionRequired && string.IsNullOrWhiteSpace(description))
            return Result.Failure(HandlerErrors.Validation("Açıklama zorunludur."));
        if (description?.Trim().Length > 300)
            return Result.Failure(HandlerErrors.Validation("Açıklama en fazla 300 karakter olabilir."));
        return null;
    }

    public static Task<CreditTransaction?> FindExisting(ICoreDbContext context, Guid businessId,
        string idempotencyKey, CancellationToken cancellationToken) => context.CreditTransactions.AsNoTracking()
        .SingleOrDefaultAsync(x => x.BusinessId == businessId && x.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public static async Task<Result<CreditAccountResponse>> Replay(ICoreDbContext context,
        BusinessLabel business, CreditTransaction existing, CreditTransactionType expectedType,
        int expectedAmount, CancellationToken cancellationToken)
    {
        if (existing.Type != expectedType || existing.Amount != expectedAmount)
            return Result<CreditAccountResponse>.Failure(HandlerErrors.Conflict(
                "Idempotency anahtarı farklı bir kredi işleminde kullanılmış."));
        BusinessCreditAccount? account = await context.BusinessCreditAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == business.Id, cancellationToken);
        return Result<CreditAccountResponse>.Success(CreditMapper.Map(account, business));
    }

    public static async Task<Result?> Save(ICoreDbContext context, CancellationToken cancellationToken)
    {
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(HandlerErrors.Conflict("Kredi bakiyesi başka bir işlem tarafından güncellendi. Tekrar deneyin."));
        }
        catch (DbUpdateException)
        {
            return Result.Failure(HandlerErrors.Conflict("Kredi işlemi daha önce işlendi veya eşzamanlı olarak değiştirildi."));
        }
        return null;
    }
}

using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Enums;
using MediatR;

namespace DeliveryOps.Core.Queries.Credits;

public sealed record CreditAccountResponse(Guid BusinessId, string BusinessName, int Balance,
    long LifetimeAdded, long LifetimeConsumed, int LowBalanceThreshold, bool IsLowBalance,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreditPackageResponse(string Code, string Name, int Amount);

public sealed record CreditTransactionResponse(Guid Id, Guid BusinessId, CreditTransactionType Type,
    int Amount, int BalanceAfter, Guid? OrderId, string Description,
    Guid CreatedByUserId, string? IdempotencyKey, DateTimeOffset CreatedAtUtc);

public sealed record GetCreditAccountQuery(Guid? BusinessId) : IRequest<Result<CreditAccountResponse>>;
public sealed record GetCreditPackagesQuery(int Page = 1, int PageSize = 20)
    : IRequest<PagedResponse<CreditPackageResponse>>;
public sealed record GetCreditTransactionsQuery(Guid? BusinessId, int Page = 1, int PageSize = 20)
    : IRequest<Result<PagedResponse<CreditTransactionResponse>>>;
public sealed record TopUpCreditsCommand(Guid? BusinessId, string PackageCode, string? Description,
    string IdempotencyKey)
    : IRequest<Result<CreditAccountResponse>>;
public sealed record AdjustCreditsCommand(Guid? BusinessId, int Amount, string Description,
    string IdempotencyKey)
    : IRequest<Result<CreditAccountResponse>>;
public sealed record RefundOrderCreditCommand(Guid? BusinessId, Guid OrderId, string Description,
    string IdempotencyKey)
    : IRequest<Result<CreditAccountResponse>>;
public sealed record UpdateCreditSettingsCommand(Guid? BusinessId, int LowBalanceThreshold)
    : IRequest<Result<CreditAccountResponse>>;

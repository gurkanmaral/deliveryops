using DeliveryOps.BuildingBlocks.Domain;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class CreditTransaction : Entity
{
    private CreditTransaction() { }

    public Guid BusinessId { get; private init; }
    public CreditTransactionType Type { get; private init; }
    public int Amount { get; private init; }
    public int BalanceAfter { get; private init; }
    public Guid? OrderId { get; private init; }
    public string Description { get; private init; } = string.Empty;
    public Guid CreatedByUserId { get; private init; }
    public string? IdempotencyKey { get; private init; }

    public static CreditTransaction Create(Guid businessId, CreditTransactionType type, int amount,
        int balanceAfter, Guid? orderId, string description, Guid createdByUserId,
        string? idempotencyKey = null)
    {
        if (businessId == Guid.Empty || amount == 0 || balanceAfter < 0)
            throw new ArgumentException("Credit transaction is invalid.");
        if (type == CreditTransactionType.OrderConsumption && (!orderId.HasValue || amount >= 0))
            throw new ArgumentException("Order consumption requires an order and a negative amount.");
        if (type == CreditTransactionType.Refund && (!orderId.HasValue || amount <= 0))
            throw new ArgumentException("Refund requires an order and a positive amount.");
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (idempotencyKey is not null && string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key is invalid.", nameof(idempotencyKey));
        return new CreditTransaction
        {
            BusinessId = businessId,
            Type = type,
            Amount = amount,
            BalanceAfter = balanceAfter,
            OrderId = orderId,
            Description = description.Trim(),
            CreatedByUserId = createdByUserId,
            IdempotencyKey = idempotencyKey?.Trim()
        };
    }
}

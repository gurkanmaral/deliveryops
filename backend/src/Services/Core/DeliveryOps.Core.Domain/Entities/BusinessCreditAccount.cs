using DeliveryOps.BuildingBlocks.Domain;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class BusinessCreditAccount : Entity
{
    private BusinessCreditAccount() { }
    private BusinessCreditAccount(Guid businessId)
    {
        BusinessId = businessId;
        LowBalanceThreshold = 100;
    }

    public Guid BusinessId { get; private init; }
    public int Balance { get; private set; }
    public long LifetimeAdded { get; private set; }
    public long LifetimeConsumed { get; private set; }
    public int LowBalanceThreshold { get; private set; }
    public uint Version { get; private set; }

    public static BusinessCreditAccount Create(Guid businessId)
    {
        if (businessId == Guid.Empty) throw new ArgumentException("Business is required.", nameof(businessId));
        return new BusinessCreditAccount(businessId);
    }

    public int Add(int amount)
    {
        if (amount is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(amount));
        Balance = checked(Balance + amount);
        LifetimeAdded += amount;
        MarkAsUpdated();
        return Balance;
    }

    public int Consume(int amount)
    {
        if (amount < 1) throw new ArgumentOutOfRangeException(nameof(amount));
        if (Balance < amount) throw new InvalidOperationException("Sipariş oluşturmak için yeterli kredi bulunmuyor.");
        Balance -= amount;
        LifetimeConsumed += amount;
        MarkAsUpdated();
        return Balance;
    }

    public int Refund(int amount)
    {
        if (amount is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(amount));
        Balance = checked(Balance + amount);
        MarkAsUpdated();
        return Balance;
    }

    public int Adjust(int amount)
    {
        if (amount is 0 or < -1_000_000 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(amount));
        if (Balance + amount < 0) throw new InvalidOperationException("Kredi bakiyesi sıfırın altına düşürülemez.");
        Balance = checked(Balance + amount);
        MarkAsUpdated();
        return Balance;
    }

    public void UpdateLowBalanceThreshold(int threshold)
    {
        if (threshold is < 0 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(threshold));
        LowBalanceThreshold = threshold;
        MarkAsUpdated();
    }
}

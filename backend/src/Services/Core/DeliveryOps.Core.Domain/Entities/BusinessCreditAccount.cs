using DeliveryOps.BuildingBlocks.Domain;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class BusinessCreditAccount : Entity
{
    /// <summary>
    /// Orders arriving from delivery platforms and the POS counter may take the balance down to this value so a
    /// business that runs out of credit does not lose orders the platform has already accepted from the customer.
    /// The next top-up settles the negative balance automatically.
    /// </summary>
    public const int IntegrationOverdraftLimit = 20;

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

    public int Consume(int amount, bool allowOverdraft = false)
    {
        if (amount < 1) throw new ArgumentOutOfRangeException(nameof(amount));
        int floor = allowOverdraft ? -IntegrationOverdraftLimit : 0;
        if (Balance - amount < floor)
            throw new InvalidOperationException(allowOverdraft
                ? $"Kredi bakiyesi ve {IntegrationOverdraftLimit} kredilik eksi bakiye limiti tükendi. Kredi yükleyin."
                : "Sipariş oluşturmak için yeterli kredi bulunmuyor.");
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
        if (amount < 0 && Balance + amount < 0) throw new InvalidOperationException("Kredi bakiyesi sıfırın altına düşürülemez.");
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

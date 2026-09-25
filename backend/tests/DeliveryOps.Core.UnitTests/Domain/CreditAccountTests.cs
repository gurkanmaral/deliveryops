using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using Xunit;

namespace DeliveryOps.Core.UnitTests.Domain;

public sealed class CreditAccountTests
{
    [Fact]
    public void One_order_consumes_one_credit()
    {
        BusinessCreditAccount account = BusinessCreditAccount.Create(Guid.NewGuid());
        account.Add(3);

        int balance = account.Consume(1);

        Assert.Equal(2, balance);
        Assert.Equal(3, account.LifetimeAdded);
        Assert.Equal(1, account.LifetimeConsumed);
    }

    [Fact]
    public void Account_cannot_be_overdrawn()
    {
        BusinessCreditAccount account = BusinessCreditAccount.Create(Guid.NewGuid());
        account.Add(1);
        account.Consume(1);

        Assert.Throws<InvalidOperationException>(() => account.Consume(1));
        Assert.Equal(0, account.Balance);
    }

    [Fact]
    public void Order_consumption_ledger_entry_requires_negative_amount_and_order()
    {
        Assert.Throws<ArgumentException>(() => CreditTransaction.Create(Guid.NewGuid(),
            CreditTransactionType.OrderConsumption, 1, 1, Guid.NewGuid(), "invalid", Guid.NewGuid()));
    }

    [Fact]
    public void Refund_restores_balance_without_changing_lifetime_top_up()
    {
        BusinessCreditAccount account = BusinessCreditAccount.Create(Guid.NewGuid());
        account.Add(2);
        account.Consume(1);

        account.Refund(1);

        Assert.Equal(2, account.Balance);
        Assert.Equal(2, account.LifetimeAdded);
        Assert.Equal(1, account.LifetimeConsumed);
    }

    [Fact]
    public void Negative_adjustment_cannot_overdraw_account()
    {
        BusinessCreditAccount account = BusinessCreditAccount.Create(Guid.NewGuid());
        account.Add(5);

        Assert.Throws<InvalidOperationException>(() => account.Adjust(-6));
        Assert.Equal(5, account.Balance);
    }

    [Fact]
    public void Low_balance_threshold_can_be_disabled_with_zero()
    {
        BusinessCreditAccount account = BusinessCreditAccount.Create(Guid.NewGuid());
        Assert.Equal(100, account.LowBalanceThreshold);

        account.UpdateLowBalanceThreshold(0);

        Assert.Equal(0, account.LowBalanceThreshold);
    }
}

using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.UnitTests.Domain;

public sealed class OrderPaymentTests
{
    [Fact]
    public void RecordPayment_marks_order_paid_and_ignores_repeated_confirmation()
    {
        Order order = CreateOrder(245.50m);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        bool first = order.RecordPayment(PaymentMethod.Card, 245.50m, PaymentChannel.Counter, "AUTH-123", now);
        bool second = order.RecordPayment(PaymentMethod.Cash, 10m, PaymentChannel.Counter, "OTHER", now.AddMinutes(1));

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(PaymentMethod.Card, order.PaymentMethod);
        Assert.Equal(245.50m, order.PaidAmount);
        Assert.Equal("AUTH-123", order.PaymentReference);
        Assert.Equal(PaymentChannel.Counter, order.PaymentChannel);
    }

    [Fact]
    public void Cash_on_delivery_requires_collection_until_paid()
    {
        Order order = CreateOrder(100m);
        order.SetPaymentMethod(PaymentMethod.Cash);
        Assert.True(order.RequiresCollectionAtDoor);

        Guid courierId = Guid.NewGuid();
        order.RecordPayment(PaymentMethod.Cash, 100m, PaymentChannel.Courier, null, DateTimeOffset.UtcNow, courierId);

        Assert.False(order.RequiresCollectionAtDoor);
        Assert.Equal(courierId, order.PaymentCollectedByCourierId);
    }

    [Fact]
    public void Online_orders_never_require_collection_at_door()
    {
        Order order = CreateOrder(100m);
        order.SetPaymentMethod(PaymentMethod.Online);

        Assert.False(order.RequiresCollectionAtDoor);
    }

    [Fact]
    public void RecordPayment_rejects_missing_method_invalid_amount_and_courier_without_id()
    {
        Order order = CreateOrder(100m);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            order.RecordPayment(PaymentMethod.Unspecified, 100m, PaymentChannel.Counter, null, now));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            order.RecordPayment(PaymentMethod.Cash, 0m, PaymentChannel.Counter, null, now));
        Assert.Throws<ArgumentException>(() =>
            order.RecordPayment(PaymentMethod.Cash, 100m, PaymentChannel.Courier, null, now));
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
    }

    [Fact]
    public void Paid_order_payment_method_cannot_be_changed()
    {
        Order order = CreateOrder(100m);
        order.RecordPayment(PaymentMethod.Card, 100m, PaymentChannel.Counter, null, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => order.SetPaymentMethod(PaymentMethod.Cash));
    }

    private static Order CreateOrder(decimal total) => Order.Create(
        Guid.NewGuid(), Guid.NewGuid(), "POS-1", "Ada Lovelace", "+905555555555",
        "Kadıköy, İstanbul", OrderSource.Pos, total, Guid.NewGuid().ToString("N"),
        new string('A', 64), Guid.NewGuid());
}

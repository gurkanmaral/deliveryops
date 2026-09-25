using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.UnitTests.Domain;

public sealed class NotificationOutboxMessageTests
{
    [Fact]
    public void Failed_notification_is_dead_lettered_and_can_be_retried()
    {
        NotificationOutboxMessage message = CreateMessage();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        message.MarkFailed("timeout", now, 2);
        message.MarkFailed("timeout", now.AddMinutes(1), 2);

        Assert.NotNull(message.DeadLetteredAtUtc);
        Assert.Equal(2, message.Attempts);

        message.Retry(now.AddMinutes(2));
        Assert.Null(message.DeadLetteredAtUtc);
        Assert.Equal(0, message.Attempts);
    }

    [Fact]
    public void Permanent_notification_failure_is_dead_lettered_immediately()
    {
        NotificationOutboxMessage message = CreateMessage();

        message.MarkFailed("unauthorized", DateTimeOffset.UtcNow, 8, 401, permanent: true);

        Assert.NotNull(message.DeadLetteredAtUtc);
        Assert.Equal(401, message.LastHttpStatusCode);
    }

    private static NotificationOutboxMessage CreateMessage()
    {
        Order order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), "PHONE-1", "Ada Lovelace",
            "+905555555555", "Kadıköy, İstanbul", OrderSource.Phone, 100,
            Guid.NewGuid().ToString("N"), new string('A', 64), Guid.NewGuid());
        order.ChangeStatus(OrderStatus.Confirmed, Guid.NewGuid());
        order.ChangeStatus(OrderStatus.WaitingForCourier, Guid.NewGuid());
        return NotificationOutboxMessage.CreateForOrder(order);
    }
}

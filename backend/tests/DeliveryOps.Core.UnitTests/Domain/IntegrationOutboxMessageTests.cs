using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.UnitTests.Domain;

public sealed class IntegrationOutboxMessageTests
{
    [Fact]
    public void Failed_delivery_is_dead_lettered_at_max_attempts()
    {
        IntegrationOutboxMessage message = CreateMessage();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        message.MarkFailed("temporary", now, 2, 409);
        Assert.Null(message.DeadLetteredAtUtc);

        message.MarkFailed("temporary", now.AddMinutes(1), 2, 409);

        Assert.Equal(2, message.Attempts);
        Assert.NotNull(message.DeadLetteredAtUtc);
        Assert.Equal(409, message.LastHttpStatusCode);
    }

    [Fact]
    public void Permanent_failure_is_dead_lettered_immediately()
    {
        IntegrationOutboxMessage message = CreateMessage();

        message.MarkFailed("forbidden", DateTimeOffset.UtcNow, 8, 403, permanent: true);

        Assert.Equal(1, message.Attempts);
        Assert.NotNull(message.DeadLetteredAtUtc);
    }

    [Fact]
    public void Manual_retry_resets_delivery_state()
    {
        IntegrationOutboxMessage message = CreateMessage();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        message.MarkFailed("bad request", now, 8, 400, permanent: true);

        message.Retry(now.AddMinutes(1));

        Assert.Equal(0, message.Attempts);
        Assert.Null(message.DeadLetteredAtUtc);
        Assert.Null(message.LastError);
        Assert.Null(message.LastHttpStatusCode);
    }

    private static IntegrationOutboxMessage CreateMessage()
    {
        Order order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), "YS-1", "Ada Lovelace",
            "+905555555555", "Kadıköy, İstanbul", OrderSource.Yemeksepeti, 100,
            Guid.NewGuid().ToString("N"), new string('A', 64), Guid.NewGuid());
        return IntegrationOutboxMessage.CreateForOrder(order);
    }
}

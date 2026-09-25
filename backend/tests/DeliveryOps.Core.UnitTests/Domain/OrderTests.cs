using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;

namespace DeliveryOps.Core.UnitTests.Domain;

public sealed class OrderTests
{
    [Fact]
    public void ChangeStatus_AllowsDefinedWorkflow()
    {
        Order order = CreateOrder();
        Guid userId = Guid.NewGuid();

        order.ChangeStatus(OrderStatus.Confirmed, userId);
        order.ChangeStatus(OrderStatus.WaitingForCourier, userId);

        Assert.Equal(OrderStatus.WaitingForCourier, order.Status);
        Assert.Equal(3, order.StatusHistory.Count);
    }

    [Fact]
    public void ChangeStatus_RejectsSkippingWorkflowSteps()
    {
        Order order = CreateOrder();

        Assert.Throws<InvalidOperationException>(() => order.ChangeStatus(OrderStatus.Delivered, Guid.NewGuid()));
    }

    [Fact]
    public void AssignCourier_PreventsSecondCourierFromClaimingOrder()
    {
        Order order = CreateOrder();
        Guid userId = Guid.NewGuid();
        order.ChangeStatus(OrderStatus.Confirmed, userId);
        order.ChangeStatus(OrderStatus.WaitingForCourier, userId);
        order.AssignCourier(Guid.NewGuid(), userId);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            order.AssignCourier(Guid.NewGuid(), userId));

        Assert.Contains("already been assigned", error.Message);
    }

    [Fact]
    public void ReassignCourier_AllowsChangeBeforePickup()
    {
        Order order = CreateOrder();
        Guid userId = Guid.NewGuid();
        Guid firstCourierId = Guid.NewGuid();
        Guid secondCourierId = Guid.NewGuid();
        order.ChangeStatus(OrderStatus.Confirmed, userId);
        order.AssignCourier(firstCourierId, userId);

        Guid previousCourierId = order.ReassignCourier(secondCourierId);

        Assert.Equal(firstCourierId, previousCourierId);
        Assert.Equal(secondCourierId, order.CourierId);
        Assert.Equal(OrderStatus.Assigned, order.Status);
    }

    [Fact]
    public void ReassignCourier_RejectsChangeAfterPickup()
    {
        Order order = CreateOrder();
        Guid userId = Guid.NewGuid();
        order.ChangeStatus(OrderStatus.Confirmed, userId);
        order.AssignCourier(Guid.NewGuid(), userId);
        order.ChangeStatus(OrderStatus.PickedUp, userId);

        Assert.Throws<InvalidOperationException>(() => order.ReassignCourier(Guid.NewGuid()));
    }

    [Fact]
    public void Cancel_StoresOperationalReason()
    {
        Order order = CreateOrder();

        order.Cancel("Müşteri vazgeçti", Guid.NewGuid());

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Müşteri vazgeçti", order.CancellationReason);
    }

    [Fact]
    public void DeliveryFailure_RequiresOnTheWayAndStoresReason()
    {
        Order order = CreateOrder();
        Guid userId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => order.ReportDeliveryFailure("Adres bulunamadı", userId));

        order.ChangeStatus(OrderStatus.Confirmed, userId);
        order.AssignCourier(Guid.NewGuid(), userId);
        order.ChangeStatus(OrderStatus.PickedUp, userId);
        order.ChangeStatus(OrderStatus.OnTheWay, userId);
        order.ReportDeliveryFailure("Adres bulunamadı", userId);

        Assert.Equal(OrderStatus.DeliveryFailed, order.Status);
        Assert.Equal("Adres bulunamadı", order.DeliveryFailureReason);
    }

    [Fact]
    public void AnonymizePii_RemovesCustomerDataFromTerminalOrder()
    {
        Order order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), string.Empty, "Ada Lovelace",
            "+905555555555", "Kadıköy, İstanbul", OrderSource.Phone, 250m,
            Guid.NewGuid().ToString("N"), new string('A', 64), Guid.NewGuid(), 40.99, 29.03,
            "Kapıcıya bırakın", DeliveryLocationSource.MapPin, DeliveryLocationAccuracy.Exact);
        order.Cancel("Müşteri vazgeçti", Guid.NewGuid());
        DateTimeOffset now = DateTimeOffset.UtcNow;

        order.AnonymizePii(now);

        Assert.Equal("Anonim Müşteri", order.CustomerName);
        Assert.Equal("-", order.CustomerPhone);
        Assert.Equal("[silindi]", order.DeliveryAddress);
        Assert.Null(order.DeliveryLatitude);
        Assert.Null(order.DeliveryLongitude);
        Assert.Null(order.DeliveryInstructions);
        Assert.Empty(order.CustomerSearchTokens);
        Assert.Equal(now, order.PiiAnonymizedAtUtc);
    }

    [Fact]
    public void AnonymizePii_RejectsActiveOrder()
    {
        Order order = CreateOrder();

        Assert.Throws<InvalidOperationException>(() => order.AnonymizePii(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Provider_courier_order_can_follow_provider_delivery_lifecycle_without_local_courier()
    {
        Order order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), "TGO-1", "Ada", "05550000000",
            "Kadıköy", OrderSource.Trendyol, 100m, Guid.NewGuid().ToString("N"),
            new string('A', 64), Guid.NewGuid(), deliveryFulfillment: DeliveryFulfillmentType.ProviderCourier);

        order.ApplyProviderDeliveryStatus(OrderStatus.OnTheWay, Guid.NewGuid());
        order.ApplyProviderDeliveryStatus(OrderStatus.Delivered, Guid.NewGuid());

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Null(order.CourierId);
    }

    private static Order CreateOrder() => Order.Create(
        Guid.NewGuid(), Guid.NewGuid(), string.Empty, "Ada Lovelace", "+905555555555",
        "Kadıköy, İstanbul", OrderSource.Phone, 250m, Guid.NewGuid().ToString("N"),
        new string('A', 64), Guid.NewGuid());
}

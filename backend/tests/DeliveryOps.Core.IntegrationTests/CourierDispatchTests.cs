using DeliveryOps.BuildingBlocks.Application;
using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Handlers.Dispatch;
using DeliveryOps.Core.Handlers.Orders;
using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.Core.Queries.Abstractions;
using DeliveryOps.Core.Queries.Dispatch;
using DeliveryOps.Core.Queries.Orders;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DeliveryOps.Core.IntegrationTests;

/// <summary>
/// End-to-end courier flows on real PostgreSQL/PostGIS: automatic candidate ranking, the package pool,
/// self-claim, capacity (several packages per courier) and two couriers racing for the same package.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CourierDispatchTests(PostgresFixture fixture)
{
    // Branch at 41.0, 29.0. ~1.4 km / ~5.5 km / ~55 km away.
    private const double NearLat = 41.01, NearLon = 29.01;
    private const double MidLat = 41.05, MidLon = 29.0;
    private const double FarLat = 41.5, FarLon = 29.0;

    [Fact]
    public async Task Automatic_dispatch_ranks_the_nearest_eligible_courier_first()
    {
        Seed seed = await SeedAsync();
        Courier near = await AddCourierAsync(seed, NearLat, NearLon);
        Courier mid = await AddCourierAsync(seed, MidLat, MidLon);
        Courier far = await AddCourierAsync(seed, FarLat, FarLon);
        Courier offShift = await AddCourierAsync(seed, NearLat, NearLon, onShift: false);
        Courier stale = await AddCourierAsync(seed, NearLat, NearLon, locationAgeMinutes: 30);
        Order order = await AddWaitingOrderAsync(seed);

        await using CoreDbContext context = fixture.CreateContext();
        IReadOnlyList<CourierSuggestionResponse> suggestions = await new DispatchCandidateFinder(context,
                TimeProvider.System, new NoRoads())
            .FindAsync(order, BusinessDispatchSettings.CreateDefault(seed.BusinessId), CancellationToken.None);

        // Outside the 10 km radius, off shift and stale positions are never suggested.
        Assert.Equal([near.Id, mid.Id], suggestions.Select(x => x.CourierId));
        Assert.DoesNotContain(suggestions, x => x.CourierId == far.Id || x.CourierId == offShift.Id || x.CourierId == stale.Id);
    }

    [Fact]
    public async Task Pool_shows_nearby_packages_and_hides_them_from_far_couriers()
    {
        Seed seed = await SeedAsync();
        Courier near = await AddCourierAsync(seed, NearLat, NearLon);
        Courier far = await AddCourierAsync(seed, FarLat, FarLon);
        Order order = await AddWaitingOrderAsync(seed);

        Result<PagedResponse<AvailableOrderResponse>> nearPool = await PoolAsync(seed, near);
        Result<PagedResponse<AvailableOrderResponse>> farPool = await PoolAsync(seed, far);

        Assert.Contains(nearPool.Value!.Items, x => x.Id == order.Id);
        Assert.InRange(nearPool.Value.Items.Single(x => x.Id == order.Id).PickupDistanceKm, 1, 2);
        Assert.Empty(farPool.Value!.Items);
        Result<OrderResponse> farClaim = await ClaimAsync(seed, far, order.Id);
        Assert.True(farClaim.IsFailure);
    }

    [Fact]
    public async Task Courier_takes_several_packages_up_to_the_capacity_limit()
    {
        Seed seed = await SeedAsync();
        Courier courier = await AddCourierAsync(seed, NearLat, NearLon);
        Order first = await AddWaitingOrderAsync(seed);
        Order second = await AddWaitingOrderAsync(seed);
        Order third = await AddWaitingOrderAsync(seed);

        Assert.True((await ClaimAsync(seed, courier, first.Id)).IsSuccess);
        Assert.True((await ClaimAsync(seed, courier, second.Id)).IsSuccess);
        Result<OrderResponse> overCapacity = await ClaimAsync(seed, courier, third.Id);

        Assert.True(overCapacity.IsFailure);
        Assert.Contains("kapasite", overCapacity.Error.Message);
        await using CoreDbContext read = fixture.CreateContext();
        Assert.Equal(2, await read.Orders.CountAsync(x => x.CourierId == courier.Id && x.Status == OrderStatus.Assigned));
        Assert.Null((await read.Orders.SingleAsync(x => x.Id == third.Id)).CourierId);
    }

    [Fact]
    public async Task Two_couriers_racing_for_one_package_get_exactly_one_assignment()
    {
        Seed seed = await SeedAsync();
        Courier first = await AddCourierAsync(seed, NearLat, NearLon);
        Courier second = await AddCourierAsync(seed, NearLat, NearLon);
        Order order = await AddWaitingOrderAsync(seed);

        Result<OrderResponse>[] results = await Task.WhenAll(
            ClaimAsync(seed, first, order.Id), ClaimAsync(seed, second, order.Id));

        Assert.Single(results, x => x.IsSuccess);
        await using CoreDbContext read = fixture.CreateContext();
        Order stored = await read.Orders.SingleAsync(x => x.Id == order.Id);
        Assert.Contains(stored.CourierId, new Guid?[] { first.Id, second.Id });
        Assert.Equal(1, await read.DispatchAttempts.CountAsync(x => x.OrderId == order.Id && x.WasSuccessful));
    }

    [Fact]
    public async Task Manual_assignment_respects_capacity_and_shift()
    {
        Seed seed = await SeedAsync();
        Courier courier = await AddCourierAsync(seed, NearLat, NearLon);
        Courier offShift = await AddCourierAsync(seed, NearLat, NearLon, onShift: false);
        Order[] orders = [await AddWaitingOrderAsync(seed), await AddWaitingOrderAsync(seed), await AddWaitingOrderAsync(seed)];

        Assert.True((await AssignAsync(seed, orders[0].Id, courier.Id)).IsSuccess);
        Assert.True((await AssignAsync(seed, orders[1].Id, courier.Id)).IsSuccess);
        Assert.True((await AssignAsync(seed, orders[2].Id, courier.Id)).IsFailure);
        Assert.True((await AssignAsync(seed, orders[2].Id, offShift.Id)).IsFailure);
    }

    private async Task<Result<PagedResponse<AvailableOrderResponse>>> PoolAsync(Seed seed, Courier courier)
    {
        await using CoreDbContext context = fixture.CreateContext();
        return await new GetAvailableOrdersHandler(context, CourierContext(seed, courier), TimeProvider.System)
            .Handle(new GetAvailableOrdersQuery(1, 100), CancellationToken.None);
    }

    private async Task<Result<OrderResponse>> ClaimAsync(Seed seed, Courier courier, Guid orderId)
    {
        await using CoreDbContext context = fixture.CreateContext();
        return await new ClaimOrderHandler(context, CourierContext(seed, courier), new NoNotifier(), TimeProvider.System)
            .Handle(new ClaimOrderCommand(orderId), CancellationToken.None);
    }

    private async Task<Result<OrderResponse>> AssignAsync(Seed seed, Guid orderId, Guid courierId)
    {
        await using CoreDbContext context = fixture.CreateContext();
        return await new AssignOrderCourierHandler(context,
                new TestContext(Guid.NewGuid(), seed.BusinessId, null, null), new NoNotifier())
            .Handle(new AssignOrderCourierCommand(orderId, courierId), CancellationToken.None);
    }

    private async Task<Seed> SeedAsync()
    {
        await using CoreDbContext context = fixture.CreateContext();
        Business business = Business.Create($"Dispatch {Guid.NewGuid():N}", $"D{Guid.NewGuid():N}"[..12]);
        Branch branch = Branch.Create(business.Id, "Merkez", "Test adresi", 41.0, 29.0);
        context.AddRange(business, branch);
        await context.SaveChangesAsync();
        return new Seed(business.Id, branch.Id);
    }

    private async Task<Courier> AddCourierAsync(Seed seed, double latitude, double longitude, bool onShift = true,
        int locationAgeMinutes = 1)
    {
        await using CoreDbContext context = fixture.CreateContext();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Courier courier = Courier.Create(seed.BusinessId, seed.BranchId, "Test", "Kurye",
            $"0555{Random.Shared.Next(1000000, 9999999)}");
        context.Couriers.Add(courier);
        if (onShift)
        {
            courier.SetAvailability(CourierAvailability.Available);
            context.CourierShifts.Add(CourierShift.Start(courier.Id, seed.BusinessId, now.AddHours(-1)));
        }
        context.CourierLocations.Add(CourierLocation.Create(courier.Id, seed.BusinessId, latitude, longitude,
            null, null, null, now.AddMinutes(-locationAgeMinutes)));
        await context.SaveChangesAsync();
        return courier;
    }

    private async Task<Order> AddWaitingOrderAsync(Seed seed)
    {
        await using CoreDbContext context = fixture.CreateContext();
        Order order = Order.Create(seed.BusinessId, seed.BranchId, $"D-{Guid.NewGuid():N}", "Test Müşteri",
            "05550000000", "Test adresi", OrderSource.Phone, 100, $"d-{Guid.NewGuid():N}", new string('A', 64),
            Guid.NewGuid());
        order.ChangeStatus(OrderStatus.Confirmed, Guid.NewGuid());
        order.ChangeStatus(OrderStatus.WaitingForCourier, Guid.NewGuid());
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }

    private static TestContext CourierContext(Seed seed, Courier courier) =>
        new(Guid.NewGuid(), seed.BusinessId, null, courier.Id);

    private sealed record Seed(Guid BusinessId, Guid BranchId);

    private sealed record TestContext(Guid UserId, Guid? BusinessId, Guid? BranchId, Guid? CourierId) : IRequestContext
    {
        public bool IsPlatformAdmin => false;
    }

    private sealed class NoNotifier : IOrderOperationsNotifier
    {
        public Task OrderChangedAsync(OrderResponse order, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OrderReassignedAsync(OrderResponse order, Guid previousCourierId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoRoads : IRoadRouteDistanceProvider
    {
        public bool IsAvailable => false;
        public string ProviderName => "none";
        public int MaxElementsPerRequest => 0;
        public Task<IReadOnlyList<RoadRouteElement>> CalculateMatrixAsync(IReadOnlyList<RoadRoutePoint> origins,
            IReadOnlyList<RoadRoutePoint> destinations, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RoadRouteElement>>([]);
    }
}

using DeliveryOps.Core.Domain.Entities;
using DeliveryOps.Core.Domain.Enums;
using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.Core.Queries.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using DeliveryOps.Core.Infrastructure.Security;
using Testcontainers.PostgreSql;
using Xunit;

namespace DeliveryOps.Core.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly OrderPiiProtector _piiProtector = new(new EphemeralDataProtectionProvider(),
        Options.Create(new OrderPiiOptions { SearchKey = "integration-test-order-search-key-32-bytes-minimum" }));
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgis/postgis:16-3.4-alpine")
        .WithDatabase("deliveryops_tests")
        .WithUsername("deliveryops")
        .WithPassword("deliveryops_tests")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using CoreDbContext context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public CoreDbContext CreateContext()
    {
        DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql(_container.GetConnectionString(), postgres => postgres.UseNetTopologySuite())
            .Options;
        return new CoreDbContext(options, new TestRequestContext(), _piiProtector);
    }

    public string CreateSearchToken(string search) => _piiProtector.CreateSearchToken(search);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private sealed class TestRequestContext : IRequestContext
    {
        public Guid UserId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000001");
        public Guid? BusinessId => null;
        public Guid? BranchId => null;
        public Guid? CourierId => null;
        public bool IsPlatformAdmin => true;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL integration";
}

[Collection(PostgresCollection.Name)]
public sealed class PostgresPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Migrations_create_postgis_enabled_schema()
    {
        await using CoreDbContext context = fixture.CreateContext();

        int migrationCount = await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM \"__EFMigrationsHistory\"").SingleAsync();
        string postgisVersion = await context.Database.SqlQueryRaw<string>(
            "SELECT PostGIS_Version() AS \"Value\"").SingleAsync();

        Assert.True(migrationCount > 0);
        Assert.NotEmpty(postgisVersion);
    }

    [Fact]
    public async Task Database_rejects_duplicate_order_idempotency_key()
    {
        (Guid businessId, Guid branchId) = await SeedBusinessAsync();
        await using (CoreDbContext first = fixture.CreateContext())
        {
            first.Orders.Add(CreateOrder(businessId, branchId, "POS-1", "same-key"));
            await first.SaveChangesAsync();
        }

        await using CoreDbContext duplicate = fixture.CreateContext();
        duplicate.Orders.Add(CreateOrder(businessId, branchId, "POS-2", "same-key"));

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Fact]
    public async Task Xmin_prevents_concurrent_credit_consumption()
    {
        (Guid businessId, _) = await SeedBusinessAsync();
        await using (CoreDbContext seed = fixture.CreateContext())
        {
            BusinessCreditAccount account = BusinessCreditAccount.Create(businessId);
            account.Add(10);
            seed.BusinessCreditAccounts.Add(account);
            await seed.SaveChangesAsync();
        }

        await using CoreDbContext first = fixture.CreateContext();
        await using CoreDbContext second = fixture.CreateContext();
        BusinessCreditAccount firstAccount = await first.BusinessCreditAccounts.SingleAsync(x => x.BusinessId == businessId);
        BusinessCreditAccount secondAccount = await second.BusinessCreditAccounts.SingleAsync(x => x.BusinessId == businessId);
        firstAccount.Consume(1);
        secondAccount.Consume(1);

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using CoreDbContext verification = fixture.CreateContext();
        Assert.Equal(9, (await verification.BusinessCreditAccounts.SingleAsync(x => x.BusinessId == businessId)).Balance);
    }

    [Fact]
    public async Task Xmin_prevents_concurrent_courier_assignment_reservations()
    {
        (Guid businessId, Guid branchId) = await SeedBusinessAsync();
        Guid courierId;
        await using (CoreDbContext seed = fixture.CreateContext())
        {
            Courier courier = Courier.Create(businessId, branchId, "Eşzamanlı", "Kurye",
                $"5{Random.Shared.NextInt64(100000000, 999999999)}");
            seed.Couriers.Add(courier);
            await seed.SaveChangesAsync();
            courierId = courier.Id;
        }

        await using CoreDbContext first = fixture.CreateContext();
        await using CoreDbContext second = fixture.CreateContext();
        Courier firstCourier = await first.Couriers.SingleAsync(x => x.Id == courierId);
        Courier secondCourier = await second.Couriers.SingleAsync(x => x.Id == courierId);
        firstCourier.ReserveAssignmentSlot();
        secondCourier.ReserveAssignmentSlot();

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Order_pii_is_encrypted_at_rest_and_decrypted_on_read()
    {
        (Guid businessId, Guid branchId) = await SeedBusinessAsync();
        Order order = CreateOrder(businessId, branchId, $"PII-{Guid.NewGuid():N}", $"pii-{Guid.NewGuid():N}");
        await using (CoreDbContext write = fixture.CreateContext())
        {
            write.Orders.Add(order);
            await write.SaveChangesAsync();
            string persistedName = await write.Database.SqlQueryRaw<string>(
                "SELECT \"CustomerName\" AS \"Value\" FROM orders WHERE \"Id\" = {0}", order.Id).SingleAsync();
            Assert.StartsWith("enc:v1:", persistedName);
            Assert.DoesNotContain("Test Müşteri", persistedName, StringComparison.Ordinal);
        }

        await using CoreDbContext read = fixture.CreateContext();
        Order restored = await read.Orders.AsNoTracking().SingleAsync(x => x.Id == order.Id);
        Assert.Equal("Test Müşteri", restored.CustomerName);
        Assert.Equal("05550000000", restored.CustomerPhone);
        Assert.Equal("Test adresi", restored.DeliveryAddress);
    }

    [Fact]
    public async Task Blind_index_supports_name_prefix_and_phone_suffix_search()
    {
        (Guid businessId, Guid branchId) = await SeedBusinessAsync();
        Order order = CreateOrder(businessId, branchId, $"SEARCH-{Guid.NewGuid():N}", $"search-{Guid.NewGuid():N}");
        await using (CoreDbContext write = fixture.CreateContext())
        {
            write.Orders.Add(order);
            await write.SaveChangesAsync();
        }

        string nameToken = fixture.CreateSearchToken("Test Mü");
        string phoneToken = fixture.CreateSearchToken("0000");
        await using CoreDbContext read = fixture.CreateContext();
        Assert.True(await read.Orders.AnyAsync(x => x.Id == order.Id && x.CustomerSearchTokens.Contains(nameToken)));
        Assert.True(await read.Orders.AnyAsync(x => x.Id == order.Id && x.CustomerSearchTokens.Contains(phoneToken)));
    }

    [Fact]
    public async Task Package_push_skips_full_and_far_couriers_but_keeps_couriers_without_position()
    {
        (Guid businessId, Guid branchId) = await SeedBusinessAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Courier near = Courier.Create(businessId, branchId, "Yakın", "Kurye", "05550000001");
        Courier far = Courier.Create(businessId, branchId, "Uzak", "Kurye", "05550000002");
        Courier busy = Courier.Create(businessId, branchId, "Dolu", "Kurye", "05550000003");
        Courier unknown = Courier.Create(businessId, branchId, "Konumsuz", "Kurye", "05550000004");
        await using (CoreDbContext context = fixture.CreateContext())
        {
            context.Couriers.AddRange(near, far, busy, unknown);
            // Branch is at 41.0, 29.0; default radius 10 km.
            context.CourierLocations.AddRange(
                CourierLocation.Create(near.Id, businessId, 41.01, 29.01, null, null, null, now.AddMinutes(-1)),
                CourierLocation.Create(far.Id, businessId, 41.0, 29.0, null, null, null, now.AddMinutes(-3)),
                CourierLocation.Create(far.Id, businessId, 41.5, 29.5, null, null, null, now.AddMinutes(-1)),
                CourierLocation.Create(busy.Id, businessId, 41.0, 29.0, null, null, null, now.AddMinutes(-1)));
            for (int i = 0; i < 2; i++)
            {
                Order order = CreateOrder(businessId, branchId, $"BUSY-{Guid.NewGuid():N}", $"busy-{Guid.NewGuid():N}");
                order.ChangeStatus(OrderStatus.Confirmed, Guid.NewGuid());
                order.ChangeStatus(OrderStatus.WaitingForCourier, Guid.NewGuid());
                order.AssignCourier(busy.Id, Guid.NewGuid());
                context.Orders.Add(order);
            }
            await context.SaveChangesAsync();
        }

        await using CoreDbContext read = fixture.CreateContext();
        Guid[] eligible = await DeliveryOps.Core.Api.Notifications.NotificationOutboxDispatcher.FilterClaimableAsync(
            read, businessId, branchId, [near.Id, far.Id, busy.Id, unknown.Id], now, CancellationToken.None);

        Assert.Equal([near.Id, unknown.Id], eligible);
    }

    private async Task<(Guid BusinessId, Guid BranchId)> SeedBusinessAsync()
    {
        await using CoreDbContext context = fixture.CreateContext();
        Business business = Business.Create($"Postgres {Guid.NewGuid():N}", $"P{Guid.NewGuid():N}"[..12]);
        Branch branch = Branch.Create(business.Id, "Merkez", "Test adresi", 41.0, 29.0);
        context.AddRange(business, branch);
        await context.SaveChangesAsync();
        return (business.Id, branch.Id);
    }

    private static Order CreateOrder(Guid businessId, Guid branchId, string externalId, string idempotencyKey) =>
        Order.Create(businessId, branchId, externalId, "Test Müşteri", "05550000000", "Test adresi",
            OrderSource.Pos, 100, idempotencyKey, new string('A', 64),
            Guid.Parse("10000000-0000-0000-0000-000000000001"));
}

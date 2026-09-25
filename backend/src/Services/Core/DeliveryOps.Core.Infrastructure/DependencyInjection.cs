using DeliveryOps.Core.Infrastructure.Persistence;
using DeliveryOps.Core.Queries.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using DeliveryOps.Core.Infrastructure.Presence;
using DeliveryOps.Core.Queries.Locations;
using DeliveryOps.Core.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;

namespace DeliveryOps.Core.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCoreInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("CoreDatabase")
            ?? throw new InvalidOperationException("Connection string 'CoreDatabase' is not configured.");

        services.AddOptions<OrderPiiOptions>()
            .Bind(configuration.GetSection("OrderPii"))
            .Validate(options => System.Text.Encoding.UTF8.GetByteCount(options.SearchKey) >= 32,
                "OrderPii:SearchKey must contain at least 32 UTF-8 bytes.")
            .ValidateOnStart();
        IDataProtectionBuilder dataProtection = services.AddDataProtection()
            .SetApplicationName("DeliveryOps.Core");
        string? keyRingPath = configuration["OrderPii:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        services.AddSingleton<IOrderPiiProtector, OrderPiiProtector>();

        services.AddDbContext<CoreDbContext>(options => options.UseNpgsql(connectionString, postgres => postgres.UseNetTopologySuite()));
        services.AddScoped<ICoreDbContext>(provider => provider.GetRequiredService<CoreDbContext>());
        string redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
        services.AddSingleton<ICourierPresenceStore, RedisCourierPresenceStore>();
        return services;
    }
}

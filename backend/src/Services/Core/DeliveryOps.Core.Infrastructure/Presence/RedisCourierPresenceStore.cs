using System.Text.Json;
using DeliveryOps.Core.Queries.Locations;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace DeliveryOps.Core.Infrastructure.Presence;

public sealed class RedisCourierPresenceStore(IConnectionMultiplexer redis, IConfiguration configuration) : ICourierPresenceStore
{
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(
        int.TryParse(configuration["CourierTracking:LatestLocationTtlMinutes"], out int minutes) ? minutes : 30);
    private static string Key(Guid courierId) => $"courier:location:{courierId:N}";

    public Task SetAsync(CourierLocationSnapshot snapshot, CancellationToken cancellationToken) =>
        redis.GetDatabase().StringSetAsync(Key(snapshot.CourierId), JsonSerializer.Serialize(snapshot), _ttl);

    public async Task<IReadOnlyDictionary<Guid, CourierLocationSnapshot>> GetAsync(IEnumerable<Guid> courierIds, CancellationToken cancellationToken)
    {
        Guid[] ids = courierIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, CourierLocationSnapshot>();
        RedisValue[] values = await redis.GetDatabase().StringGetAsync(ids.Select(id => (RedisKey)Key(id)).ToArray());
        Dictionary<Guid, CourierLocationSnapshot> result = [];
        for (int i = 0; i < ids.Length; i++)
        {
            if (!values[i].HasValue) continue;
            CourierLocationSnapshot? snapshot = JsonSerializer.Deserialize<CourierLocationSnapshot>(values[i]!);
            if (snapshot is not null) result[ids[i]] = snapshot;
        }
        return result;
    }
}

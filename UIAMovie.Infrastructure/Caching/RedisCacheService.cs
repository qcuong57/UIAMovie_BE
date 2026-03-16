using StackExchange.Redis;
using Newtonsoft.Json;
using UIAMovie.Application.Interfaces;

namespace UIAMovie.Infrastructure.Caching;



public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IDatabase _db;

    public RedisCacheService(IConnectionMultiplexer connectionMultiplexer)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _db = connectionMultiplexer.GetDatabase();
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var value = await _db.StringGetAsync(key);
        if (value.IsNull)
            return default;

        return JsonConvert.DeserializeObject<T>(value.ToString());
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var json = JsonConvert.SerializeObject(value);

        var expiration = expiry.HasValue
            ? new Expiration(expiry.Value)
            : Expiration.Persist;

        await _db.StringSetAsync(key, json, expiration, ValueCondition.Always);
    }

    public async Task RemoveAsync(string key)
    {
        await _db.KeyDeleteAsync(key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        return await _db.KeyExistsAsync(key);
    }
}
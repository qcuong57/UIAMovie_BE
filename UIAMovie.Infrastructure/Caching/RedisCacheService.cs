// UIAMovie.Infrastructure/Caching/RedisCacheService.cs
using StackExchange.Redis;
using Newtonsoft.Json;
using UIAMovie.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace UIAMovie.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer    _redis;
    private readonly ILogger<RedisCacheService> _logger;

    // Upstash free: 256 KB per value. Cảnh báo nếu vượt quá.
    private const int MAX_VALUE_BYTES = 200_000;

    public RedisCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisCacheService> logger)
    {
        _redis  = redis;
        _logger = logger;
    }

    // ── Lấy database an toàn ─────────────────────────────────────────────────

    private IDatabase? TryGetDb()
    {
        try
        {
            return _redis.IsConnected ? _redis.GetDatabase() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Redis] Không thể lấy database");
            return null;
        }
    }

    // ── Get ──────────────────────────────────────────────────────────────────

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var db  = TryGetDb();
            if (db == null) return default;

            var val = await db.StringGetAsync(key);
            if (val.IsNull) return default;

            return JsonConvert.DeserializeObject<T>(val.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Redis] GetAsync '{Key}' thất bại", key);
            return default;
        }
    }

    // ── Set ──────────────────────────────────────────────────────────────────

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        try
        {
            var db = TryGetDb();
            if (db == null) return;

            var json = JsonConvert.SerializeObject(value,
                new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });

            // Cảnh báo nếu value quá lớn (Upstash limit)
            if (json.Length > MAX_VALUE_BYTES)
            {
                _logger.LogWarning(
                    "[Redis] Key '{Key}' có size {Size} bytes > {Max} bytes — bỏ qua cache",
                    key, json.Length, MAX_VALUE_BYTES);
                return;
            }

            await db.StringSetAsync(key, json,
                expiry.HasValue ? new Expiration(expiry.Value) : Expiration.Persist,
                ValueCondition.Always);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Redis] SetAsync '{Key}' thất bại", key);
        }
    }

    // ── Remove single ────────────────────────────────────────────────────────

    public async Task RemoveAsync(string key)
    {
        try
        {
            var db = TryGetDb();
            if (db == null) return;
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Redis] RemoveAsync '{Key}' thất bại", key);
        }
    }

    // ── Remove many (pipeline) ───────────────────────────────────────────────
    //
    // Thay vì N lần await KeyDeleteAsync() tuần tự (N × round-trip),
    // dùng pipeline gửi tất cả lệnh trong 1 round-trip duy nhất.
    // Với Upstash (~80ms/call), xóa 5 key:
    //   Trước: 5 × 80ms = 400ms
    //   Sau:   1 × 80ms =  80ms  +  tiết kiệm 4 commands quota

    public async Task RemoveManyAsync(params string[] keys)
    {
        if (keys == null || keys.Length == 0) return;
        if (keys.Length == 1) { await RemoveAsync(keys[0]); return; }

        try
        {
            var db = TryGetDb();
            if (db == null) return;

            var batch = db.CreateBatch();

            var tasks = keys
                .Where(k => !string.IsNullOrEmpty(k))
                .Select(k => batch.KeyDeleteAsync(k))
                .ToList();

            batch.Execute(); // gửi tất cả trong 1 round-trip
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Redis] RemoveManyAsync thất bại ({Count} keys)", keys.Length);
        }
    }

    // ── Exists ───────────────────────────────────────────────────────────────

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            var db = TryGetDb();
            if (db == null) return false;
            return await db.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Redis] ExistsAsync '{Key}' thất bại", key);
            return false;
        }
    }
}
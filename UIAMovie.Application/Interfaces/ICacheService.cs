// UIAMovie.Application/Interfaces/ICacheService.cs
namespace UIAMovie.Application.Interfaces;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    Task RemoveAsync(string key);

    /// <summary>
    /// Xóa nhiều key trong 1 round-trip duy nhất (pipeline).
    /// Dùng thay cho nhiều RemoveAsync() tuần tự — tiết kiệm commands Upstash.
    /// </summary>
    Task RemoveManyAsync(params string[] keys);

    Task<bool> ExistsAsync(string key);

    /// <summary>
    /// Get từ cache — miss thì gọi factory, set rồi trả về.
    /// Tránh lặp get/null-check/set ở khắp nơi và giảm round-trip.
    /// </summary>
    async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<Task<T?>> factory,
        TimeSpan? expiry = null)
    {
        var cached = await GetAsync<T>(key);
        if (cached != null) return cached;

        var value = await factory();
        if (value != null)
            await SetAsync(key, value, expiry);

        return value;
    }
}
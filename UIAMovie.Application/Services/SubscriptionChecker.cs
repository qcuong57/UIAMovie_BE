// UIAMovie.Application/Services/SubscriptionChecker.cs
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

/// <summary>
/// Implementation của ISubscriptionChecker.
/// Luôn kiểm tra ExpiredAt thực tế từ bảng UserSubscription,
/// không tin vào User.SubscriptionType vì có thể đã hết hạn nhưng chưa được reset.
/// </summary>
public class SubscriptionChecker : ISubscriptionChecker
{
    private readonly IRepository<UserSubscription> _subRepo;
    private readonly IRepository<User>             _userRepo;
    private readonly ICacheService                 _cache;

    private const string SUB_CACHE_KEY       = "subscription:{0}";
    private const string IS_PREMIUM_CACHE_KEY = "subscription:{0}:isPremium"; // phải khớp với PaymentService.InvalidateUserCacheAsync

    public SubscriptionChecker(
        IRepository<UserSubscription> subRepo,
        IRepository<User>             userRepo,
        ICacheService                 cache)
    {
        _subRepo  = subRepo;
        _userRepo = userRepo;
        _cache    = cache;
    }

    /// <summary>
    /// True nếu user đang có Premium HỢP LỆ (chưa hết hạn).
    /// Kết quả được cache 5 phút để tránh query DB liên tục.
    /// </summary>
    public async Task<bool> IsPremiumAsync(Guid userId)
    {
        var cacheKey = string.Format(IS_PREMIUM_CACHE_KEY, userId);
        var cached   = await _cache.GetAsync<bool?>(cacheKey);
        if (cached.HasValue) return cached.Value;

        var subs = await _subRepo.FindAsync(s => s.UserId == userId);
        var sub  = subs.FirstOrDefault();

        var now       = DateTime.UtcNow;
        var isPremium = sub?.SubscriptionType == "Premium" && sub.ExpiredAt > now;

        await _cache.SetAsync(cacheKey, isPremium, TimeSpan.FromMinutes(5));
        return isPremium;
    }

    /// <summary>
    /// True nếu user có thể xem content Premium:
    ///   - Tài khoản phải active (không bị ban)
    ///   - Subscription còn hạn
    /// </summary>
    public async Task<bool> CanWatchPremiumContentAsync(Guid userId)
    {
        // Kiểm tra tài khoản còn active không
        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null || !user.IsActive) return false;

        return await IsPremiumAsync(userId);
    }
}
// UIAMovie.Application/Interfaces/IBannerContextProvider.cs
using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Interfaces;

/// <summary>Người xem banner: khách (UserId = null) hoặc user đã đăng nhập.</summary>
public sealed record BannerViewer(Guid? UserId, string? Email, bool IsPremium)
{
    public static readonly BannerViewer Guest = new(null, null, false);
    public bool IsGuest => UserId == null;
}

/// <summary>Cầu nối Application → Infrastructure: thông tin người xem và gói được gắn vào banner.</summary>
public interface IBannerContextProvider
{
    Task<BannerViewer> GetViewerAsync(Guid? userId, CancellationToken ct = default);

    /// <summary>Chỉ trả các gói còn bán (bật + trong StartsAt..EndsAt), kèm IsEligible theo viewer.</summary>
    Task<Dictionary<string, BannerPlanDTO>> GetPlansAsync(
        IEnumerable<string> planIds, BannerViewer viewer, CancellationToken ct = default);

    Task<bool> PlanExistsAsync(string planId, CancellationToken ct = default);
}
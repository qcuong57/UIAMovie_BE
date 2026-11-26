// UIAMovie.Infrastructure/Data/BannerContextProvider.cs
//
// GIẢ ĐỊNH (chỉnh nếu khác): entity gói là `SubscriptionPlan` (UIAMovie.Domain.Entities) với các thuộc tính
// PlanId, Name, PriceVnd, OriginalPriceVnd, DurationDays, IsActive, IsTrial, MaxPerUser,
// AllowedEmailDomains (string, ngăn cách bằng , ; hoặc khoảng trắng), StartsAt, EndsAt  — khớp với AdminPlanDTO.
// Entity User có Id (Guid) và Email.

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data;

namespace UIAMovie.Infrastructure.Data;

public class BannerContextProvider : IBannerContextProvider
{
    private readonly MovieDbContext _db;

    public BannerContextProvider(MovieDbContext db) => _db = db;

    public async Task<BannerViewer> GetViewerAsync(Guid? userId, CancellationToken ct = default)
    {
        if (userId == null) return BannerViewer.Guest;
        var uid = userId.Value;
        var now = DateTime.UtcNow;

        var email = await _db.Set<User>().AsNoTracking()
            .Where(u => u.Id == uid).Select(u => u.Email).FirstOrDefaultAsync(ct);

        var premium = await _db.Set<UserSubscription>().AsNoTracking()
            .AnyAsync(s => s.UserId == uid
                           && s.SubscriptionType == SubscriptionTypes.Premium
                           && s.ExpiredAt != null && s.ExpiredAt > now, ct);

        return new BannerViewer(uid, email, premium);
    }

    public async Task<Dictionary<string, BannerPlanDTO>> GetPlansAsync(
        IEnumerable<string> planIds, BannerViewer viewer, CancellationToken ct = default)
    {
        var ids = planIds.Distinct().ToList();
        var now = DateTime.UtcNow;

        // Chỉ gói còn bán: bật, và đang trong khung StartsAt..EndsAt
        var plans = await _db.Set<SubscriptionPlan>().AsNoTracking()
            .Where(p => ids.Contains(p.PlanId)
                        && p.IsActive
                        && (p.StartsAt == null || p.StartsAt <= now)
                        && (p.EndsAt == null || p.EndsAt >= now))
            .ToListAsync(ct);

        // Số lần user đã mua thành công mỗi gói (cho MaxPerUser / trial)
        var used = new Dictionary<string, int>();
        if (viewer.UserId != null && plans.Count > 0)
        {
            var uid = viewer.UserId.Value;
            used = await _db.Set<PaymentOrder>().AsNoTracking()
                .Where(o => o.UserId == uid && ids.Contains(o.PlanId) && o.Status == PaymentStatus.Success)
                .GroupBy(o => o.PlanId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        }

        var result = new Dictionary<string, BannerPlanDTO>();
        foreach (var p in plans)
        {
            used.TryGetValue(p.PlanId, out var usedCount);
            result[p.PlanId] = ToDto(p, IsEligible(p, viewer, usedCount));
        }
        return result;
    }

    public Task<bool> PlanExistsAsync(string planId, CancellationToken ct = default)
        => _db.Set<SubscriptionPlan>().AsNoTracking().AnyAsync(p => p.PlanId == planId, ct);

    // ── Điều kiện mua (khớp quy tắc server chặn ở create-order) ──────────────

    private static bool IsEligible(SubscriptionPlan p, BannerViewer viewer, int usedCount)
    {
        // Khách: luôn coi là đủ điều kiện (server vẫn chặn ở create-order sau khi đăng nhập)
        if (viewer.IsGuest) return true;

        if (p.IsTrial && viewer.IsPremium) return false;

        var limit = p.MaxPerUser ?? (p.IsTrial ? 1 : (int?)null);
        if (limit != null && usedCount >= limit) return false;

        var domains = ParseDomains(p.AllowedEmailDomains);
        if (domains.Count > 0 && !EmailMatches(viewer.Email, domains)) return false;

        return true;
    }

    private static List<string> ParseDomains(string? raw)
        => (raw ?? string.Empty)
            .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim().TrimStart('@').ToLowerInvariant())
            .Where(d => d.Length > 0)
            .ToList();

    /// <summary>"sv.uia.edu.vn" khớp domain "uia.edu.vn" hoặc "edu.vn" (đúng ranh giới dấu chấm).</summary>
    private static bool EmailMatches(string? email, List<string> domains)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        if (email == null || at < 0) return false;
        var host = email[(at + 1)..].ToLowerInvariant();
        return domains.Any(d => host == d || host.EndsWith("." + d));
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private static string Vnd(long v)
        => v.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.') + "đ";

    private static BannerPlanDTO ToDto(SubscriptionPlan p, bool eligible)
    {
        int? discount = null;
        if (p.OriginalPriceVnd is > 0 && p.OriginalPriceVnd > p.PriceVnd)
            discount = (int)Math.Round((1 - (double)p.PriceVnd / p.OriginalPriceVnd.Value) * 100);

        return new BannerPlanDTO
        {
            PlanId = p.PlanId,
            Name = p.Name,
            PriceVnd = p.PriceVnd,
            OriginalPriceVnd = p.OriginalPriceVnd,
            PriceDisplay = p.PriceVnd == 0 ? "Miễn phí" : Vnd(p.PriceVnd),
            OriginalPriceDisplay = discount != null ? Vnd(p.OriginalPriceVnd!.Value) : null,
            DiscountPercent = discount,
            DurationDays = p.DurationDays,
            IsTrial = p.IsTrial,
            EndsAt = p.EndsAt,
            IsEligible = eligible
        };
    }
}
// UIAMovie.Application/Services/SubscriptionPlanService.cs

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

public interface ISubscriptionPlanService
{
    /// <summary>Tìm gói theo mã. onlyOnSale = true: chỉ trả khi gói đang bán (bật + trong khoảng StartsAt/EndsAt).</summary>
    Task<SubscriptionPlan?> FindByPlanIdAsync(string planId, bool onlyOnSale);

    /// <summary>Các gói đang bán, đã sắp xếp theo SortOrder rồi giá.</summary>
    Task<List<SubscriptionPlan>> GetOnSalePlansAsync();

    Task<List<AdminPlanDTO>> AdminListAsync();
    Task<AdminPlanDTO> AdminCreateAsync(UpsertPlanDTO dto);
    Task<AdminPlanDTO> AdminUpdateAsync(Guid id, UpsertPlanDTO dto);
    Task AdminSetActiveAsync(Guid id, bool isActive);
}

public static class SubscriptionPlanMapper
{
    public static SubscriptionPlanDTO ToPublicDto(SubscriptionPlan p) => new()
    {
        PlanId = p.PlanId,
        Name = p.Name,
        Description = p.Description,
        PriceVnd = p.PriceVnd,
        OriginalPriceVnd = p.OriginalPriceVnd,
        PriceDisplay = FormatPrice(p),
        DurationDays = p.DurationDays,
        IsMostPopular = p.IsMostPopular,
        Features = p.GetFeatures(),
        IsTrial = p.IsTrial,
        AllowedEmailDomains = SplitDomains(p.AllowedEmailDomains),
        EndsAt = p.EndsAt
    };

    public static AdminPlanDTO ToAdminDto(SubscriptionPlan p) => new()
    {
        Id = p.Id,
        PlanId = p.PlanId,
        Name = p.Name,
        Description = p.Description,
        PriceVnd = p.PriceVnd,
        OriginalPriceVnd = p.OriginalPriceVnd,
        DurationDays = p.DurationDays,
        IsMostPopular = p.IsMostPopular,
        IsActive = p.IsActive,
        SortOrder = p.SortOrder,
        Features = p.GetFeatures(),
        IsTrial = p.IsTrial,
        MaxPerUser = p.MaxPerUser,
        AllowedEmailDomains = p.AllowedEmailDomains,
        StartsAt = p.StartsAt,
        EndsAt = p.EndsAt
    };

    public static List<string> SplitDomains(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string FormatPrice(SubscriptionPlan p)
    {
        if (p.PriceVnd == 0) return $"Miễn phí {p.DurationDays} ngày";

        var money = p.PriceVnd.ToString("N0", new CultureInfo("vi-VN")) + "đ";
        var suffix = p.DurationDays switch
        {
            30 => "/tháng",
            365 => "/năm",
            _ => $"/{p.DurationDays} ngày"
        };
        return money + suffix;
    }
}

public class SubscriptionPlanService : ISubscriptionPlanService
{
    private static readonly Regex PlanIdRegex = new("^[a-z0-9_]{3,50}$", RegexOptions.Compiled);

    private readonly IRepository<SubscriptionPlan> _repo;
    private readonly ILogger<SubscriptionPlanService> _logger;

    public SubscriptionPlanService(IRepository<SubscriptionPlan> repo, ILogger<SubscriptionPlanService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <summary>Đang được bán: bật + nằm trong khoảng StartsAt/EndsAt (UTC).</summary>
    public static bool IsOnSale(SubscriptionPlan p, DateTime nowUtc) =>
        p.IsActive
        && (p.StartsAt is not { } s || s <= nowUtc)
        && (p.EndsAt is not { } e || e > nowUtc);

    public async Task<SubscriptionPlan?> FindByPlanIdAsync(string planId, bool onlyOnSale)
    {
        var key = (planId ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length == 0) return null;

        var plan = (await _repo.FindAsync(p => p.PlanId == key)).FirstOrDefault();
        if (plan == null) return null;

        return onlyOnSale && !IsOnSale(plan, DateTime.UtcNow) ? null : plan;
    }

    public async Task<List<SubscriptionPlan>> GetOnSalePlansAsync()
    {
        var now = DateTime.UtcNow;
        return (await _repo.FindAsync(p => p.IsActive))
            .Where(p => IsOnSale(p, now))
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.PriceVnd)
            .ToList();
    }

    // ── Admin ────────────────────────────────────────────────────────────────

    public async Task<List<AdminPlanDTO>> AdminListAsync() =>
        (await _repo.FindAsync(_ => true))
        .OrderBy(p => p.SortOrder)
        .ThenBy(p => p.PriceVnd)
        .Select(SubscriptionPlanMapper.ToAdminDto)
        .ToList();

    public async Task<AdminPlanDTO> AdminCreateAsync(UpsertPlanDTO dto)
    {
        var planId = (dto.PlanId ?? string.Empty).Trim().ToLowerInvariant();
        if (!PlanIdRegex.IsMatch(planId))
            throw new ArgumentException("Mã gói chỉ gồm a-z, 0-9, dấu gạch dưới (3–50 ký tự).");
        if (await FindByPlanIdAsync(planId, onlyOnSale: false) != null)
            throw new ArgumentException($"Mã gói '{planId}' đã tồn tại.");

        Validate(dto);

        var plan = new SubscriptionPlan { PlanId = planId };
        Apply(plan, dto);

        await _repo.AddAsync(plan);
        if (plan.IsMostPopular) await ClearOtherPopularAsync(plan.Id);
        await _repo.SaveChangesAsync();

        _logger.LogInformation("[Admin] Created plan {PlanId}", planId);
        return SubscriptionPlanMapper.ToAdminDto(plan);
    }

    public async Task<AdminPlanDTO> AdminUpdateAsync(Guid id, UpsertPlanDTO dto)
    {
        var plan = await _repo.GetByIdAsync(id) ?? throw new ArgumentException("Không tìm thấy gói.");
        Validate(dto);

        Apply(plan, dto); // PlanId KHÔNG đổi
        plan.UpdatedAt = DateTime.UtcNow;

        if (plan.IsMostPopular) await ClearOtherPopularAsync(plan.Id);
        await _repo.SaveChangesAsync();

        _logger.LogInformation("[Admin] Updated plan {PlanId}", plan.PlanId);
        return SubscriptionPlanMapper.ToAdminDto(plan);
    }

    public async Task AdminSetActiveAsync(Guid id, bool isActive)
    {
        var plan = await _repo.GetByIdAsync(id) ?? throw new ArgumentException("Không tìm thấy gói.");
        plan.IsActive = isActive;
        plan.UpdatedAt = DateTime.UtcNow;
        await _repo.SaveChangesAsync();

        _logger.LogInformation("[Admin] Plan {PlanId} active={Active}", plan.PlanId, isActive);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void Validate(UpsertPlanDTO d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) throw new ArgumentException("Tên gói không được trống.");
        if (d.DurationDays is < 1 or > 3650) throw new ArgumentException("Số ngày phải từ 1 đến 3650.");
        if (d.PriceVnd < 0) throw new ArgumentException("Giá không hợp lệ.");
        // 10.000đ là mức an toàn; chỉnh theo giới hạn tối thiểu thực tế của VNPay/SePay bạn dùng
        if (d.PriceVnd is > 0 and < 10_000) throw new ArgumentException("Gói trả phí tối thiểu 10.000đ.");
        if (d.OriginalPriceVnd is { } o && o <= d.PriceVnd)
            throw new ArgumentException("Giá gốc phải lớn hơn giá bán.");
        if (d.MaxPerUser is < 1) throw new ArgumentException("Số lần tối đa mỗi user phải >= 1.");
        if (d.StartsAt is { } s && d.EndsAt is { } e && e.ToUniversalTime() <= s.ToUniversalTime())
            throw new ArgumentException("Ngày kết thúc phải sau ngày bắt đầu.");
    }

    private static void Apply(SubscriptionPlan p, UpsertPlanDTO d)
    {
        var free = d.PriceVnd == 0;

        p.Name = d.Name.Trim();
        p.Description = (d.Description ?? string.Empty).Trim();
        p.PriceVnd = d.PriceVnd;
        p.OriginalPriceVnd = d.OriginalPriceVnd;
        p.DurationDays = d.DurationDays;
        p.IsMostPopular = d.IsMostPopular;
        p.IsActive = d.IsActive;
        p.SortOrder = d.SortOrder;

        // Gói 0đ luôn là gói dùng thử và BẮT BUỘC có giới hạn (tránh Premium miễn phí vô hạn)
        p.IsTrial = free || d.IsTrial;
        p.MaxPerUser = free ? (d.MaxPerUser ?? 1) : d.MaxPerUser;

        p.AllowedEmailDomains = NormalizeDomains(d.AllowedEmailDomains);
        p.StartsAt = d.StartsAt?.ToUniversalTime();
        p.EndsAt = d.EndsAt?.ToUniversalTime();

        var features = (d.Features ?? new List<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim());
        p.FeaturesJson = JsonSerializer.Serialize(features);
    }

    private static string? NormalizeDomains(string? csv)
    {
        var list = SubscriptionPlanMapper.SplitDomains(csv)
            .Select(x => x.TrimStart('@', '.').ToLowerInvariant())
            .Where(x => x.Length > 0)
            .Distinct()
            .ToList();
        return list.Count == 0 ? null : string.Join(",", list);
    }

    /// <summary>Chỉ một gói được gắn nhãn "phổ biến nhất".</summary>
    private async Task ClearOtherPopularAsync(Guid keepId)
    {
        foreach (var other in await _repo.FindAsync(x => x.IsMostPopular && x.Id != keepId))
            other.IsMostPopular = false;
    }
}
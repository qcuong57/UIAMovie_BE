// UIAMovie.Application/Services/BannerService.cs
//
// Cache: "banners:active:{placement}" = danh sách banner IsActive của placement (TTL 5 phút), chưa lọc theo người xem.
// Mỗi request mới lọc trong bộ nhớ:
//   1) khung giờ StartAt/EndAt (không cần job nền)
//   2) Audience (Guest / Free / Premium)
//   3) Gói gắn kèm: gói tắt / hết hạn / user không đủ điều kiện → ẩn banner
// Mọi thao tác ghi → xoá cache toàn bộ placement.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

public class BannerService : IBannerService
{
    private readonly IBannerRepository _repo;
    private readonly ICloudinaryService _cloudinary;
    private readonly ICacheService _cache;
    private readonly IBannerContextProvider _ctx;
    private readonly ILogger<BannerService> _logger;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private const int MaxEventsPerBatch = 50;
    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly TimeZoneInfo VnTz = ResolveVnTz();

    public BannerService(
        IBannerRepository repo,
        ICloudinaryService cloudinary,
        ICacheService cache,
        IBannerContextProvider ctx,
        ILogger<BannerService> logger)
    {
        _repo = repo;
        _cloudinary = cloudinary;
        _cache = cache;
        _ctx = ctx;
        _logger = logger;
    }

    // ── Public ────────────────────────────────────────────────────────────────

    public async Task<List<BannerDTO>> GetActiveAsync(BannerPlacement placement, Guid? userId = null)
    {
        var key = CacheKey(placement);
        var all = await _cache.GetAsync<List<AdminBannerDTO>>(key);
        if (all == null)
        {
            all = (await _repo.GetActiveByPlacementAsync(placement)).Select(ToAdmin).ToList();
            await _cache.SetAsync(key, all, CacheTtl);
        }

        var now = DateTime.UtcNow;
        var live = all
            .Where(b => (b.StartAt == null || b.StartAt <= now) && (b.EndAt == null || b.EndAt >= now))
            .ToList();
        if (live.Count == 0) return new List<BannerDTO>();

        var viewer = await _ctx.GetViewerAsync(userId);
        live = live.Where(b => AudienceMatches(b.Audience, viewer)).ToList();

        var planIds = live.Where(b => b.PlanId != null).Select(b => b.PlanId!).Distinct().ToList();
        var plans = planIds.Count > 0
            ? await _ctx.GetPlansAsync(planIds, viewer)
            : new Dictionary<string, BannerPlanDTO>();

        var result = new List<BannerDTO>();
        foreach (var b in live)
        {
            BannerPlanDTO? plan = null;
            if (b.PlanId != null)
            {
                // Gói không còn bán (tắt / ngoài khung giờ / bị xoá) hoặc user không mua được → ẩn banner
                if (!plans.TryGetValue(b.PlanId, out plan) || !plan.IsEligible) continue;
            }
            result.Add(ToPublic(b, b.ShowPlanPrice ? plan : null));
            if (result.Count >= BannerPlacementRules.MaxItems(placement)) break;
        }
        return result;
    }

    public async Task<Guid?> ResolveAttributionAsync(Guid? bannerId, string planId)
    {
        if (bannerId is null) return null;
        var b = await _repo.GetByIdAsync(bannerId.Value);
        return b != null && string.Equals(b.PlanId, planId, StringComparison.OrdinalIgnoreCase)
            ? b.Id : null;
    }

    // ── Admin ─────────────────────────────────────────────────────────────────

    public async Task<AdminBannerDTO?> GetByIdAsync(Guid id)
    {
        var b = await _repo.GetByIdAsync(id);
        return b == null ? null : WithStatus(ToAdmin(b));
    }

    public async Task<(IEnumerable<AdminBannerDTO> Items, int Total)> GetPagedAsync(FilterBannersDTO filter)
    {
        var (items, total) = await _repo.GetPagedAsync(filter);
        return (items.Select(b => WithStatus(ToAdmin(b))).ToList(), total);
    }

    public async Task<Guid> CreateAsync(CreateBannerDTO dto)
    {
        // ── Validate TOÀN BỘ trước khi upload để không để lại file mồ côi ──
        var title = CleanText(dto.Title, "Tiêu đề", 150, required: true)!;
        var subtitle = CleanText(dto.Subtitle, "Subtitle", 300);
        var cta = CleanText(dto.CtaText, "CtaText", 40);
        var badge = CleanText(dto.BadgeText, "BadgeText", 20);
        var alt = CleanText(dto.AltText, "AltText", 200);
        var bg = RequireColor(dto.BackgroundColor);

        if (!Enum.IsDefined(dto.Placement)) throw new ArgumentException("Vị trí không hợp lệ.");
        if (!Enum.IsDefined(dto.Audience)) throw new ArgumentException("Đối tượng không hợp lệ.");
        if (dto.ImageFile == null && string.IsNullOrWhiteSpace(dto.ImageUrl))
            throw new ArgumentException("Phải cung cấp ImageFile hoặc ImageUrl.");

        var start = ToUtc(dto.StartAt);
        var end = ToUtc(dto.EndAt);
        ValidateRange(start, end);

        var link = RequireLink(dto.LinkUrl, "LinkUrl");
        var imageUrl = dto.ImageFile == null ? RequireImageUrl(dto.ImageUrl, "ImageUrl") : null;
        var mobileUrl = dto.MobileImageFile == null ? RequireImageUrl(dto.MobileImageUrl, "MobileImageUrl") : null;
        ValidateSize(dto.ImageWidth, dto.ImageHeight);
        ValidateSize(dto.MobileImageWidth, dto.MobileImageHeight);
        var fit = RequireFit(dto.FitMode) ?? BannerFitModes.Contain;
        ValidateMaxHeight(dto.MaxHeight);
        var device = RequireDevice(dto.Device) ?? BannerDevices.All;
        var genres = NormalizeGenres(dto.TargetGenres);
        var abGroup = NormalizeAbGroup(dto.AbGroup);
        ValidateFrequency(dto.MaxShows, dto.ShowIntervalHours,
            dto.DisplayDelaySeconds, dto.DismissCooldownHours, dto.InteractionCooldownHours);
        var targetPages = NormalizeTargetPages(dto.TargetPages);

        var planId = Clean(dto.PlanId);
        await ValidatePlanAsync(planId, dto.ShowPlanPrice);

        // ── Upload ──
        string? imageId = null, mobileId = null;
        try
        {
            if (dto.ImageFile != null)
            {
                imageUrl = await _cloudinary.UploadImageAsync(dto.ImageFile, "uiamovie/banners");
                imageId = ExtractId(imageUrl);
            }
            if (dto.MobileImageFile != null)
            {
                mobileUrl = await _cloudinary.UploadImageAsync(dto.MobileImageFile, "uiamovie/banners/mobile");
                mobileId = ExtractId(mobileUrl);
            }

            var banner = new Banner
            {
                Title = title,
                Subtitle = subtitle,
                CtaText = cta,
                BadgeText = badge,
                AltText = alt,
                ImageUrl = imageUrl!,
                ImageCloudinaryPublicId = imageId,
                ImageWidth = dto.ImageWidth,
                ImageHeight = dto.ImageHeight,
                MobileImageUrl = mobileUrl,
                MobileImageCloudinaryPublicId = mobileId,
                MobileImageWidth = mobileUrl == null ? null : dto.MobileImageWidth,
                MobileImageHeight = mobileUrl == null ? null : dto.MobileImageHeight,
                BackgroundColor = bg,
                FitMode = fit,
                MaxHeight = dto.MaxHeight,
                Device = device,
                TargetGenres = genres,
                AbGroup = abGroup,
                MaxShows = dto.MaxShows,
                ShowIntervalHours = dto.ShowIntervalHours,
                DisplayDelaySeconds = dto.DisplayDelaySeconds,
                DismissCooldownHours = dto.DismissCooldownHours,
                InteractionCooldownHours = dto.InteractionCooldownHours,
                TargetPages = targetPages,
                LinkUrl = link,
                OpenInNewTab = dto.OpenInNewTab,
                Placement = dto.Placement,
                Audience = dto.Audience,
                StartAt = start,
                EndAt = end,
                DisplayOrder = dto.DisplayOrder,
                IsActive = dto.IsActive,
                PlanId = planId,
                ShowPlanPrice = dto.ShowPlanPrice
            };

            var id = await _repo.AddAsync(banner);
            await InvalidateAsync();
            return id;
        }
        catch
        {
            // Upload/ghi DB lỗi giữa chừng → dọn file vừa upload
            await TryDeleteAsync(imageId);
            await TryDeleteAsync(mobileId);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateBannerDTO dto)
    {
        var b = await _repo.GetByIdAsync(id);
        if (b == null) return false;

        // ── Validate + gán field (chưa upload gì) ──
        if (dto.Title != null) b.Title = CleanText(dto.Title, "Tiêu đề", 150, required: true)!;
        if (dto.Subtitle != null) b.Subtitle = CleanText(dto.Subtitle, "Subtitle", 300);
        if (dto.CtaText != null) b.CtaText = CleanText(dto.CtaText, "CtaText", 40);
        if (dto.BadgeText != null) b.BadgeText = CleanText(dto.BadgeText, "BadgeText", 20);
        if (dto.AltText != null) b.AltText = CleanText(dto.AltText, "AltText", 200);
        if (dto.BackgroundColor != null) b.BackgroundColor = RequireColor(dto.BackgroundColor);

        if (dto.Placement.HasValue)
        {
            if (!Enum.IsDefined(dto.Placement.Value)) throw new ArgumentException("Vị trí không hợp lệ.");
            b.Placement = dto.Placement.Value;
        }
        if (dto.Audience.HasValue)
        {
            if (!Enum.IsDefined(dto.Audience.Value)) throw new ArgumentException("Đối tượng không hợp lệ.");
            b.Audience = dto.Audience.Value;
        }
        if (dto.DisplayOrder.HasValue) b.DisplayOrder = dto.DisplayOrder.Value;
        if (dto.IsActive.HasValue) b.IsActive = dto.IsActive.Value;
        if (dto.OpenInNewTab.HasValue) b.OpenInNewTab = dto.OpenInNewTab.Value;
        if (dto.ShowPlanPrice.HasValue) b.ShowPlanPrice = dto.ShowPlanPrice.Value;

        if (dto.PlanId != null) b.PlanId = Clean(dto.PlanId);
        await ValidatePlanAsync(b.PlanId, b.ShowPlanPrice);

        if (dto.ClearLink) b.LinkUrl = null;
        else if (dto.LinkUrl != null) b.LinkUrl = RequireLink(dto.LinkUrl, "LinkUrl");

        if (dto.ClearStartAt) b.StartAt = null;
        else if (dto.StartAt.HasValue) b.StartAt = ToUtc(dto.StartAt);
        if (dto.ClearEndAt) b.EndAt = null;
        else if (dto.EndAt.HasValue) b.EndAt = ToUtc(dto.EndAt);
        ValidateRange(b.StartAt, b.EndAt);

        if (dto.ImageWidth.HasValue || dto.ImageHeight.HasValue)
        {
            ValidateSize(dto.ImageWidth, dto.ImageHeight);
            b.ImageWidth = dto.ImageWidth ?? b.ImageWidth;
            b.ImageHeight = dto.ImageHeight ?? b.ImageHeight;
        }
        if (dto.MobileImageWidth.HasValue || dto.MobileImageHeight.HasValue)
        {
            ValidateSize(dto.MobileImageWidth, dto.MobileImageHeight);
            b.MobileImageWidth = dto.MobileImageWidth ?? b.MobileImageWidth;
            b.MobileImageHeight = dto.MobileImageHeight ?? b.MobileImageHeight;
        }

        var newFit = RequireFit(dto.FitMode);
        if (newFit != null) b.FitMode = newFit;
        if (dto.ClearMaxHeight) b.MaxHeight = null;
        else if (dto.MaxHeight.HasValue) { ValidateMaxHeight(dto.MaxHeight); b.MaxHeight = dto.MaxHeight; }

        var newDevice = RequireDevice(dto.Device);
        if (newDevice != null) b.Device = newDevice;
        if (dto.TargetGenres != null) b.TargetGenres = NormalizeGenres(dto.TargetGenres);   // "" = xoá
        if (dto.AbGroup != null) b.AbGroup = NormalizeAbGroup(dto.AbGroup);                 // "" = xoá
        if (dto.TargetPages != null) b.TargetPages = NormalizeTargetPages(dto.TargetPages); // "" = xoá
        if (dto.ClearFrequency)
        {
            b.MaxShows = null; b.ShowIntervalHours = null;
            b.DisplayDelaySeconds = null; b.DismissCooldownHours = null; b.InteractionCooldownHours = null;
        }
        else if (dto.MaxShows.HasValue || dto.ShowIntervalHours.HasValue || dto.DisplayDelaySeconds.HasValue
                 || dto.DismissCooldownHours.HasValue || dto.InteractionCooldownHours.HasValue)
        {
            ValidateFrequency(dto.MaxShows, dto.ShowIntervalHours,
                dto.DisplayDelaySeconds, dto.DismissCooldownHours, dto.InteractionCooldownHours);
            if (dto.MaxShows.HasValue) b.MaxShows = dto.MaxShows;
            if (dto.ShowIntervalHours.HasValue) b.ShowIntervalHours = dto.ShowIntervalHours;
            if (dto.DisplayDelaySeconds.HasValue) b.DisplayDelaySeconds = dto.DisplayDelaySeconds;
            if (dto.DismissCooldownHours.HasValue) b.DismissCooldownHours = dto.DismissCooldownHours;
            if (dto.InteractionCooldownHours.HasValue) b.InteractionCooldownHours = dto.InteractionCooldownHours;
        }

        var newImageUrl = dto.ImageFile == null ? RequireImageUrl(dto.ImageUrl, "ImageUrl") : null;
        var newMobileUrl = dto.MobileImageFile == null ? RequireImageUrl(dto.MobileImageUrl, "MobileImageUrl") : null;

        // ── Ảnh: file cũ chỉ bị xoá SAU khi đã lưu DB ──
        string? oldImageId = null, oldMobileId = null;
        string? uploadedImageId = null, uploadedMobileId = null; // để dọn nếu lưu DB lỗi

        try
        {
            if (dto.ImageFile != null)
            {
                oldImageId = b.ImageCloudinaryPublicId;
                b.ImageUrl = await _cloudinary.UploadImageAsync(dto.ImageFile, "uiamovie/banners");
                b.ImageCloudinaryPublicId = uploadedImageId = ExtractId(b.ImageUrl);
            }
            else if (newImageUrl != null && newImageUrl != b.ImageUrl)
            {
                oldImageId = b.ImageCloudinaryPublicId;
                b.ImageUrl = newImageUrl;
                b.ImageCloudinaryPublicId = null;
            }

            if (dto.MobileImageFile != null)
            {
                oldMobileId = b.MobileImageCloudinaryPublicId;
                b.MobileImageUrl = await _cloudinary.UploadImageAsync(dto.MobileImageFile, "uiamovie/banners/mobile");
                b.MobileImageCloudinaryPublicId = uploadedMobileId = ExtractId(b.MobileImageUrl);
            }
            else if (newMobileUrl != null && newMobileUrl != b.MobileImageUrl)
            {
                oldMobileId = b.MobileImageCloudinaryPublicId;
                b.MobileImageUrl = newMobileUrl;
                b.MobileImageCloudinaryPublicId = null;
            }
            else if (dto.ClearMobileImage)
            {
                oldMobileId = b.MobileImageCloudinaryPublicId;
                b.MobileImageUrl = null;
                b.MobileImageCloudinaryPublicId = null;
                b.MobileImageWidth = null;
                b.MobileImageHeight = null;
            }

            await _repo.UpdateAsync(b);
        }
        catch
        {
            await TryDeleteAsync(uploadedImageId);
            await TryDeleteAsync(uploadedMobileId);
            throw;
        }

        await InvalidateAsync();
        await TryDeleteAsync(oldImageId);
        await TryDeleteAsync(oldMobileId);
        return true;
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive)
    {
        var b = await _repo.GetByIdAsync(id);
        if (b == null) return false;
        if (b.IsActive == isActive) return true;
        b.IsActive = isActive;
        await _repo.UpdateAsync(b);
        await InvalidateAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var b = await _repo.GetByIdAsync(id);
        if (b == null) return false;

        await _repo.DeleteAsync(id);
        await InvalidateAsync();

        await TryDeleteAsync(b.ImageCloudinaryPublicId);
        await TryDeleteAsync(b.MobileImageCloudinaryPublicId);
        return true;
    }

    // ── Tracking ──────────────────────────────────────────────────────────────

    public async Task RecordEventsAsync(IEnumerable<BannerEventInputDTO> events, Guid? userId)
    {
        var valid = events
            .Where(e => e != null && e.BannerId != Guid.Empty && BannerEventTypes.IsValid(e.Type))
            // Chống ghi trùng (React StrictMode / retry) trong cùng 1 lô: cùng banner + loại + phiên
            .DistinctBy(e => (e.BannerId, e.Type, e.SessionId))
            .Take(MaxEventsPerBatch)
            .ToList();
        if (valid.Count == 0) return;

        // Bỏ event của banner không tồn tại (tránh lỗi FK và dữ liệu rác)
        var existing = await _repo.GetExistingIdsAsync(valid.Select(e => e.BannerId).Distinct());
        var now = DateTime.UtcNow;
        var rows = valid
            .Where(e => existing.Contains(e.BannerId))
            .Select(e => new BannerEvent
            {
                BannerId = e.BannerId,
                Type = e.Type,
                UserId = userId,
                SessionId = e.SessionId is { Length: > 0 } s ? (s.Length > 64 ? s[..64] : s) : null,
                CreatedAt = now
            })
            .ToList();

        if (rows.Count > 0) await _repo.AddEventsAsync(rows);
    }

    public async Task<List<BannerStatsDTO>> GetStatsAsync(DateTime? fromUtc, DateTime? toUtcExclusive)
    {
        var rows = await _repo.GetStatsAsync(fromUtc, toUtcExclusive);
        return rows
            .Select(r => new BannerStatsDTO
            {
                BannerId = r.BannerId,
                Title = r.Title,
                Views = r.Views,
                Clicks = r.Clicks,
                Dismisses = r.Dismisses,
                Conversions = r.Conversions,
                Ctr = r.Views == 0 ? 0 : Math.Round((double)r.Clicks / r.Views, 4),
                OrdersCreated = r.OrdersCreated,
                OrdersPaid = r.OrdersPaid,
                Revenue = r.Revenue,
                ConversionRate = r.Clicks == 0 ? 0 : Math.Round((double)r.OrdersPaid / r.Clicks, 4)
            })
            .OrderByDescending(s => s.Revenue).ThenByDescending(s => s.Views)
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string CacheKey(BannerPlacement p) => $"banners:active:{p}";

    private Task InvalidateAsync()
        => _cache.RemoveManyAsync(Enum.GetValues<BannerPlacement>().Select(CacheKey).ToArray());

    private string? ExtractId(string url)
        => _cloudinary.ExtractPublicId(url)
           ?? Path.GetFileNameWithoutExtension(new Uri(url).AbsolutePath.Split('/').Last());

    private async Task TryDeleteAsync(string? publicId)
    {
        if (string.IsNullOrEmpty(publicId)) return;
        try { await _cloudinary.DeleteFileAsync(publicId, "image"); }
        catch (Exception ex) { _logger.LogWarning(ex, "[BannerService] Không xóa được ảnh Cloudinary {Id}", publicId); }
    }

    private static bool AudienceMatches(BannerAudience a, BannerViewer v) => a switch
    {
        BannerAudience.All => true,
        BannerAudience.Guest => v.IsGuest,
        BannerAudience.Free => !v.IsGuest && !v.IsPremium,
        BannerAudience.Premium => !v.IsGuest && v.IsPremium,
        _ => false
    };

    private async Task ValidatePlanAsync(string? planId, bool showPlanPrice)
    {
        if (planId == null)
        {
            if (showPlanPrice) throw new ArgumentException("ShowPlanPrice cần PlanId.");
            return;
        }
        if (planId.Length > 100) throw new ArgumentException("PlanId quá dài.");
        if (!await _ctx.PlanExistsAsync(planId))
            throw new ArgumentException($"Không tìm thấy gói '{planId}'.");
    }

    // ── Thời gian ──

    private static TimeZoneInfo ResolveVnTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
            catch { return TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "VN", "VN"); }
        }
    }

    /// <summary>
    /// Utc → giữ; Local (có offset trong chuỗi ISO) → đổi sang UTC;
    /// Unspecified (không có "Z"/offset) → hiểu là GIỜ VIỆT NAM (không phụ thuộc múi giờ server).
    /// </summary>
    private static DateTime? ToUtc(DateTime? d)
    {
        if (d == null) return null;
        var v = d.Value;
        return v.Kind switch
        {
            DateTimeKind.Utc => v,
            DateTimeKind.Local => v.ToUniversalTime(),
            _ => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(v, DateTimeKind.Unspecified), VnTz)
        };
    }

    private static void ValidateRange(DateTime? start, DateTime? end)
    {
        if (start.HasValue && end.HasValue && end < start)
            throw new ArgumentException("EndAt phải sau StartAt.");
    }

    // ── Chuẩn hoá input ──

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? CleanText(string? raw, string field, int max, bool required = false)
    {
        var v = Clean(raw);
        if (v == null)
        {
            if (required) throw new ArgumentException($"{field} không được để trống.");
            return null;
        }
        if (v.Length > max) throw new ArgumentException($"{field} tối đa {max} ký tự.");
        return v;
    }

    private static string? RequireColor(string? raw)
    {
        var v = Clean(raw);
        if (v == null) return null;
        if (!HexColor.IsMatch(v)) throw new ArgumentException("BackgroundColor phải dạng #RRGGBB.");
        return v.ToLowerInvariant();
    }

    private static string? RequireFit(string? raw)
    {
        var v = Clean(raw)?.ToLowerInvariant();
        if (v == null) return null;
        if (!BannerFitModes.IsValid(v)) throw new ArgumentException("Kiểu hiển thị ảnh không hợp lệ (contain | cover).");
        return v;
    }

    private static string? RequireDevice(string? raw)
    {
        var v = Clean(raw)?.ToLowerInvariant();
        if (v == null) return null;
        if (!BannerDevices.IsValid(v)) throw new ArgumentException("Thiết bị không hợp lệ (all | desktop | mobile).");
        return v;
    }

    /// <summary>"Hành động, kinh dị ,Hành động" → "Hành động,kinh dị" (bỏ trùng, tối đa 10 thể loại, mỗi tên ≤ 40 ký tự). Rỗng → null.</summary>
    private static string? NormalizeGenres(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var list = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) return null;
        if (list.Count > 10) throw new ArgumentException("Tối đa 10 thể loại.");
        if (list.Any(g => g.Length > 40)) throw new ArgumentException("Tên thể loại tối đa 40 ký tự.");
        return string.Join(",", list);
    }

    private static string? NormalizeAbGroup(string? raw)
    {
        var v = Clean(raw);
        if (v != null && v.Length > 40) throw new ArgumentException("Tên nhóm A/B tối đa 40 ký tự.");
        return v;
    }

    private static void ValidateFrequency(int? maxShows, int? intervalHours,
        int? delaySeconds = null, int? dismissHours = null, int? interactionHours = null)
    {
        if (delaySeconds.HasValue && (delaySeconds < 0 || delaySeconds > 300)) throw new ArgumentException("Thời gian chờ trước khi hiện phải từ 0 đến 300 giây.");
        if (dismissHours.HasValue && (dismissHours < 0 || dismissHours > 8760)) throw new ArgumentException("Thời gian ẩn sau khi đóng phải từ 0 đến 8760 giờ.");
        if (interactionHours.HasValue && (interactionHours < 0 || interactionHours > 8760)) throw new ArgumentException("Thời gian ẩn sau khi tương tác phải từ 0 đến 8760 giờ.");
        if (maxShows.HasValue && (maxShows < 1 || maxShows > 20)) throw new ArgumentException("Số lần hiện tối đa phải từ 1 đến 20.");
        if (intervalHours.HasValue && (intervalHours < 0 || intervalHours > 8760)) throw new ArgumentException("Khoảng cách giữa 2 lần hiện phải từ 0 đến 8760 giờ.");
    }

    /// <summary>"/, /movies ,/movies" → "/,/movies". Chỉ đường dẫn nội bộ; tối đa 20 mục, mỗi mục ≤ 40 ký tự. Rỗng → null (mọi trang).</summary>
    private static string? NormalizeTargetPages(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var list = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) return null;
        if (list.Count > 20) throw new ArgumentException("Tối đa 20 trang đích.");
        foreach (var p in list)
        {
            if (p.Length > 40 || !p.StartsWith('/') || p.StartsWith("//") || p.Contains('\\') || p.Any(char.IsWhiteSpace))
                throw new ArgumentException($"Trang đích '{p}' không hợp lệ (dạng /duong-dan, ≤ 40 ký tự, không khoảng trắng).");
        }
        return string.Join(",", list);
    }

    private static void ValidateMaxHeight(int? h)
    {
        if (h.HasValue && (h < 80 || h > 2000)) throw new ArgumentException("Chiều cao tối đa phải từ 80 đến 2000 px.");
    }

    private static void ValidateSize(int? w, int? h)
    {
        if (w.HasValue && (w < 1 || w > 8000)) throw new ArgumentException("Chiều rộng ảnh không hợp lệ.");
        if (h.HasValue && (h < 1 || h > 8000)) throw new ArgumentException("Chiều cao ảnh không hợp lệ.");
    }

    private static string? RequireHttpUrl(string? raw, string field, bool httpsOnly = false)
    {
        var v = Clean(raw);
        if (v == null) return null;
        if (v.Length > 2000) throw new ArgumentException($"{field} quá dài.");
        if (!Uri.TryCreate(v, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"{field} phải là URL http/https hợp lệ.");
        if (httpsOnly && uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException($"{field} phải dùng https (http sẽ bị trình duyệt chặn trên trang https).");
        return v;
    }

    /// <summary>Ảnh dán bằng URL: bắt buộc https để tránh mixed-content.</summary>
    private static string? RequireImageUrl(string? raw, string field) => RequireHttpUrl(raw, field, httpsOnly: true);

    /// <summary>Link click: http(s) hoặc đường dẫn nội bộ "/..." (không cho "//host", "\" và "javascript:").</summary>
    private static string? RequireLink(string? raw, string field)
    {
        var v = Clean(raw);
        if (v == null) return null;
        if (v.StartsWith('/') && !v.StartsWith("//") && !v.Contains('\\')) return v;
        return RequireHttpUrl(v, field);
    }

    /// <summary>
    /// LinkUrl cho FE:
    ///  - rỗng + có PlanId → /packages?plan={PlanId} (deep link, FE chọn sẵn gói)
    ///  - link nội bộ → gắn thêm banner={id} để FE gửi lại khi tạo đơn (đo chuyển đổi)
    /// </summary>
    private static string? ResolveLink(Banner b)
    {
        var link = b.LinkUrl;
        if (link == null && b.PlanId != null)
            link = $"/packages?plan={Uri.EscapeDataString(b.PlanId)}";
        if (link != null && link.StartsWith('/') && !link.Contains("banner="))
            link += (link.Contains('?') ? "&" : "?") + $"banner={b.Id}";
        return link;
    }

    // ── Mapping ──

    private static AdminBannerDTO ToAdmin(Banner b) => new()
    {
        Id = b.Id,
        Title = b.Title,
        Subtitle = b.Subtitle,
        CtaText = b.CtaText,
        BadgeText = b.BadgeText,
        AltText = b.AltText ?? b.Title,
        ImageUrl = b.ImageUrl,
        ImageWidth = b.ImageWidth,
        ImageHeight = b.ImageHeight,
        MobileImageUrl = b.MobileImageUrl,
        MobileImageWidth = b.MobileImageWidth,
        MobileImageHeight = b.MobileImageHeight,
        BackgroundColor = b.BackgroundColor,
        FitMode = b.FitMode,
        MaxHeight = b.MaxHeight,
        Device = b.Device,
        TargetGenres = b.TargetGenres,
        AbGroup = b.AbGroup,
        MaxShows = b.MaxShows,
        ShowIntervalHours = b.ShowIntervalHours,
        DisplayDelaySeconds = b.DisplayDelaySeconds,
        DismissCooldownHours = b.DismissCooldownHours,
        InteractionCooldownHours = b.InteractionCooldownHours,
        TargetPages = b.TargetPages,
        LinkUrl = ResolveLink(b),
        RawLinkUrl = b.LinkUrl,
        OpenInNewTab = b.OpenInNewTab,
        Placement = b.Placement,
        Audience = b.Audience,
        StartAt = b.StartAt,
        EndAt = b.EndAt,
        DisplayOrder = b.DisplayOrder,
        IsActive = b.IsActive,
        PlanId = b.PlanId,
        ShowPlanPrice = b.ShowPlanPrice,
        CreatedAt = b.CreatedAt,
        UpdatedAt = b.UpdatedAt
    };

    private static AdminBannerDTO WithStatus(AdminBannerDTO d)
    {
        var now = DateTime.UtcNow;
        d.Status = !d.IsActive ? "inactive"
            : d.StartAt > now ? "scheduled"
            : d.EndAt < now ? "expired"
            : "live";
        return d;
    }

    private static BannerDTO ToPublic(AdminBannerDTO b, BannerPlanDTO? plan) => new()
    {
        Id = b.Id,
        Title = b.Title,
        Subtitle = b.Subtitle,
        CtaText = b.CtaText,
        BadgeText = b.BadgeText,
        AltText = b.AltText,
        ImageUrl = b.ImageUrl,
        ImageWidth = b.ImageWidth,
        ImageHeight = b.ImageHeight,
        MobileImageUrl = b.MobileImageUrl,
        MobileImageWidth = b.MobileImageWidth,
        MobileImageHeight = b.MobileImageHeight,
        BackgroundColor = b.BackgroundColor,
        FitMode = b.FitMode,
        MaxHeight = b.MaxHeight,
        Device = b.Device,
        TargetGenres = b.TargetGenres,
        AbGroup = b.AbGroup,
        MaxShows = b.MaxShows,
        ShowIntervalHours = b.ShowIntervalHours,
        // Popup: áp mặc định ở BE để FE không phải hard-code thời gian
        DisplayDelaySeconds = b.Placement == BannerPlacement.Popup
            ? b.DisplayDelaySeconds ?? BannerPopupDefaults.DisplayDelaySeconds : (int?)null,
        DismissCooldownHours = b.Placement == BannerPlacement.Popup
            ? b.DismissCooldownHours ?? BannerPopupDefaults.DismissCooldownHours : (int?)null,
        InteractionCooldownHours = b.Placement == BannerPlacement.Popup
            ? b.InteractionCooldownHours ?? BannerPopupDefaults.InteractionCooldownHours : (int?)null,
        TargetPages = b.TargetPages,
        LinkUrl = b.LinkUrl,
        OpenInNewTab = b.OpenInNewTab,
        Placement = b.Placement,
        StartAt = b.StartAt,
        EndAt = b.EndAt,
        DisplayOrder = b.DisplayOrder,
        Plan = plan
    };
}
// UIAMovie.Application/Services/AdService.cs
//
// Business logic cho hệ thống Global Ads.
//
// Caching strategy:
//   - GetAdsForContentAsync → cache Redis 5 phút per contentType.
//     Vì global ads không thay đổi theo từng contentId, ta cache theo contentType
//     ("ads:global:Movie", "ads:global:Episode"...) — share giữa mọi content cùng loại.
//     → Tiết kiệm bộ nhớ cache hơn so với cache per-content.
//   - Khi có content-specific override → cache per (contentType, contentId) riêng.
//   - Mọi write (create/update/delete slot) → invalidate cache theo contentType liên quan.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Application.Options;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

public class AdService : IAdService
{
    private readonly IAdRepository _adRepo;
    private readonly ICloudinaryService _cloudinary;
    private readonly ICacheService _cache;
    private readonly ILogger<AdService> _logger;
    private readonly AdScheduleOptions _scheduleOptions;

    private static readonly TimeSpan AdCacheTtl = TimeSpan.FromMinutes(5);

    private const string ScheduleSettingsKey = "ads:schedule-settings";

    // Player chỉ phát 2 loại: phim lẻ và TẬP phim. TvShow là phạm vi "cả bộ" (áp cho mọi tập của bộ đó).
    private static readonly AdContentType[] PlayableTypes = { AdContentType.Movie, AdContentType.Episode };
    private const int MaxDurationSeconds = 24 * 3600; // chặn giá trị bậy từ endpoint public
    private static readonly TimeSpan ScheduleSettingsTtl = TimeSpan.FromDays(365);

    public AdService(
        IAdRepository adRepo,
        ICloudinaryService cloudinary,
        ICacheService cache,
        ILogger<AdService> logger,
        IOptions<AdScheduleOptions> scheduleOptions)
    {
        _adRepo = adRepo;
        _cloudinary = cloudinary;
        _cache = cache;
        _logger = logger;
        _scheduleOptions = scheduleOptions.Value;
    }

    // ── Ad CRUD ───────────────────────────────────────────────────────────────

    public async Task<AdDTO?> GetAdByIdAsync(Guid id)
    {
        var ad = await _adRepo.GetByIdWithSlotsAsync(id);
        return ad == null ? null : MapToAdDTO(ad);
    }

    public async Task<(IEnumerable<AdDTO> Items, int Total)> GetAdsAsync(FilterAdsDTO filter)
    {
        var (items, total) = await _adRepo.GetPagedAsync(filter);
        return (items.Select(MapToAdDTO), total);
    }

    public async Task<Guid> CreateAdAsync(CreateAdDTO dto)
    {
        if (dto.VideoFile == null && string.IsNullOrWhiteSpace(dto.VideoUrl))
            throw new ArgumentException("Phải cung cấp VideoFile hoặc VideoUrl.");

        if (dto.BrandImageFile == null && string.IsNullOrWhiteSpace(dto.BrandImageUrl))
            throw new ArgumentException("Phải cung cấp BrandImageFile hoặc BrandImageUrl (ảnh nhãn hiệu).");

        string? videoUrl = dto.VideoUrl;
        string? cloudinaryPublicId = null;

        if (dto.VideoFile != null)
        {
            videoUrl = await _cloudinary.UploadVideoAsync(dto.VideoFile, "uiamovie/ads");
            cloudinaryPublicId = Path.GetFileNameWithoutExtension(
                new Uri(videoUrl).AbsolutePath.Split('/').Last());
        }

        string? brandImageUrl = dto.BrandImageUrl;
        string? brandImageCloudinaryPublicId = null;

        if (dto.BrandImageFile != null)
        {
            brandImageUrl = await _cloudinary.UploadImageAsync(dto.BrandImageFile, "uiamovie/ads/brand");
            brandImageCloudinaryPublicId = Path.GetFileNameWithoutExtension(
                new Uri(brandImageUrl).AbsolutePath.Split('/').Last());
        }

        var ad = new Advertisement
        {
            Title = dto.Title.Trim(),
            VideoUrl = videoUrl,
            CloudinaryPublicId = cloudinaryPublicId,
            BrandImageUrl = brandImageUrl,
            BrandImageCloudinaryPublicId = brandImageCloudinaryPublicId,
            DurationSeconds = dto.DurationSeconds,
            SkipAfterSeconds = dto.SkipAfterSeconds,
            ClickThroughUrl = dto.ClickThroughUrl?.Trim()
        };

        return await _adRepo.AddAsync(ad);
    }

    public async Task<bool> UpdateAdAsync(Guid id, UpdateAdDTO dto)
    {
        var ad = await _adRepo.GetByIdAsync(id);
        if (ad == null) return false;

        if (dto.Title != null) ad.Title = dto.Title.Trim();
        if (dto.IsActive != null) ad.IsActive = dto.IsActive.Value;
        if (dto.ClickThroughUrl != null)
            ad.ClickThroughUrl = string.IsNullOrWhiteSpace(dto.ClickThroughUrl)
                ? null
                : dto.ClickThroughUrl.Trim();

        if (dto.DurationSeconds != null) ad.DurationSeconds = dto.DurationSeconds.Value;
        if (dto.SkipAfterSeconds != null) ad.SkipAfterSeconds = dto.SkipAfterSeconds;

        if (dto.VideoFile != null)
        {
            if (!string.IsNullOrEmpty(ad.CloudinaryPublicId))
            {
                try
                {
                    await _cloudinary.DeleteFileAsync(ad.CloudinaryPublicId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[AdService] Không xóa được Cloudinary file {Id}", ad.CloudinaryPublicId);
                }
            }

            ad.VideoUrl = await _cloudinary.UploadVideoAsync(dto.VideoFile, "uiamovie/ads");
            ad.CloudinaryPublicId = Path.GetFileNameWithoutExtension(
                new Uri(ad.VideoUrl).AbsolutePath.Split('/').Last());
        }
        else if (!string.IsNullOrWhiteSpace(dto.VideoUrl))
        {
            ad.VideoUrl = dto.VideoUrl.Trim();
            ad.CloudinaryPublicId = null;
        }

        if (dto.BrandImageFile != null)
        {
            if (!string.IsNullOrEmpty(ad.BrandImageCloudinaryPublicId))
            {
                try
                {
                    await _cloudinary.DeleteFileAsync(ad.BrandImageCloudinaryPublicId, "image");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[AdService] Không xóa được ảnh nhãn hiệu cũ trên Cloudinary {Id}",
                        ad.BrandImageCloudinaryPublicId);
                }
            }

            ad.BrandImageUrl = await _cloudinary.UploadImageAsync(dto.BrandImageFile, "uiamovie/ads/brand");
            ad.BrandImageCloudinaryPublicId = Path.GetFileNameWithoutExtension(
                new Uri(ad.BrandImageUrl).AbsolutePath.Split('/').Last());
        }
        else if (!string.IsNullOrWhiteSpace(dto.BrandImageUrl))
        {
            ad.BrandImageUrl = dto.BrandImageUrl.Trim();
            ad.BrandImageCloudinaryPublicId = null;
        }

        await _adRepo.UpdateAsync(ad);

        // Invalidate cache toàn bộ contentType (vì global ad thay đổi ảnh hưởng tất cả)
        await InvalidateGlobalCacheAsync();

        return true;
    }

    public async Task<bool> DeleteAdAsync(Guid id)
    {
        var ad = await _adRepo.GetByIdWithSlotsAsync(id);
        if (ad == null) return false;

        // Xóa DB trước: nếu lỗi thì file Cloudinary vẫn còn, ad không bị "mất video"
        await _adRepo.DeleteAsync(id);

        // Invalidate SAU khi xóa, tránh request chen giữa nạp lại cache có ad cũ
        await InvalidateGlobalCacheAsync();

        if (!string.IsNullOrEmpty(ad.CloudinaryPublicId))
        {
            try
            {
                await _cloudinary.DeleteFileAsync(ad.CloudinaryPublicId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AdService] Không xóa được Cloudinary file {Id}", ad.CloudinaryPublicId);
            }
        }

        if (!string.IsNullOrEmpty(ad.BrandImageCloudinaryPublicId))
        {
            try
            {
                await _cloudinary.DeleteFileAsync(ad.BrandImageCloudinaryPublicId, "image");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AdService] Không xóa được ảnh nhãn hiệu Cloudinary {Id}",
                    ad.BrandImageCloudinaryPublicId);
            }
        }

        return true;
    }

    // ── Global Slots ──────────────────────────────────────────────────────────

    public async Task<Guid> CreateGlobalSlotAsync(Guid adId, CreateGlobalSlotDTO dto)
    {
        var ad = await _adRepo.GetByIdAsync(adId);
        if (ad == null)
            throw new KeyNotFoundException($"Không tìm thấy ad {adId}");

        if (dto.Position == AdPosition.MidRoll && !dto.MidRollOffsetSeconds.HasValue)
            throw new ArgumentException("MidRollOffsetSeconds bắt buộc khi Position = MidRoll.");

        var slot = new GlobalAdSlot
        {
            AdvertisementId = adId,
            AppliesTo = dto.AppliesTo,
            Position = dto.Position,
            MidRollOffsetSeconds = dto.Position == AdPosition.MidRoll
                ? dto.MidRollOffsetSeconds
                : null,
            DisplayOrder = dto.DisplayOrder
        };

        var slotId = await _adRepo.AddSlotAsync(slot);

        // Invalidate cache contentType bị ảnh hưởng
        await InvalidateCacheForSlotAsync(dto.AppliesTo);

        return slotId;
    }

    public async Task<bool> UpdateGlobalSlotAsync(Guid slotId, UpdateGlobalSlotDTO dto)
    {
        var slot = await _adRepo.GetSlotByIdAsync(slotId);
        if (slot == null) return false;

        var oldAppliesTo = slot.AppliesTo;

        if (dto.AppliesTo != null) slot.AppliesTo = dto.AppliesTo;
        if (dto.IsActive != null) slot.IsActive = dto.IsActive.Value;
        if (dto.DisplayOrder != null) slot.DisplayOrder = dto.DisplayOrder.Value;

        if (dto.Position.HasValue)
        {
            slot.Position = dto.Position.Value;
            if (dto.Position.Value != AdPosition.MidRoll)
                slot.MidRollOffsetSeconds = null;
        }

        if (dto.MidRollOffsetSeconds.HasValue && slot.Position == AdPosition.MidRoll)
            slot.MidRollOffsetSeconds = dto.MidRollOffsetSeconds.Value;

        await _adRepo.UpdateSlotAsync(slot);

        // Invalidate cả old và new AppliesTo (nếu đổi scope)
        await InvalidateCacheForSlotAsync(oldAppliesTo);
        if (slot.AppliesTo != oldAppliesTo)
            await InvalidateCacheForSlotAsync(slot.AppliesTo);

        return true;
    }

    public async Task<bool> DeleteGlobalSlotAsync(Guid slotId)
    {
        var slot = await _adRepo.GetSlotByIdAsync(slotId);
        if (slot == null) return false;

        await _adRepo.DeleteSlotAsync(slotId);
        await InvalidateCacheForSlotAsync(slot.AppliesTo);

        return true;
    }

    // ── Content-specific Override ─────────────────────────────────────────────

    public async Task<Guid> CreateOverrideAsync(Guid adId, CreateOverrideDTO dto)
    {
        var ad = await _adRepo.GetByIdAsync(adId);
        if (ad == null)
            throw new KeyNotFoundException($"Không tìm thấy ad {adId}");

        if (dto.Position == AdPosition.MidRoll && !dto.MidRollOffsetSeconds.HasValue)
            throw new ArgumentException("MidRollOffsetSeconds bắt buộc khi Position = MidRoll.");

        var ov = new AdContentOverride
        {
            AdvertisementId = adId,
            ContentType = dto.ContentType,
            ContentId = dto.ContentId,
            Position = dto.Position,
            MidRollOffsetSeconds = dto.Position == AdPosition.MidRoll
                ? dto.MidRollOffsetSeconds
                : null,
            DisplayOrder = dto.DisplayOrder
        };

        var ovId = await _adRepo.AddOverrideAsync(ov);

        // Override chỉ ảnh hưởng 1 content cụ thể → invalidate content cache
        await _cache.RemoveAsync(BuildContentCacheKey(dto.ContentType, dto.ContentId));

        return ovId;
    }

    public async Task<bool> DeleteOverrideAsync(Guid overrideId)
    {
        var ov = await _adRepo.GetOverrideByIdAsync(overrideId);
        if (ov == null) return false;

        await _adRepo.DeleteOverrideAsync(overrideId);
        await _cache.RemoveAsync(BuildContentCacheKey(ov.ContentType, ov.ContentId));

        return true;
    }

    // ── Player API ────────────────────────────────────────────────────────────

    public async Task<ContentAdsDTO> GetAdsForContentAsync(
        AdContentType contentType,
        Guid contentId,
        int? durationSeconds = null,
        Guid? parentId = null)
    {
        var duration = Math.Clamp(durationSeconds ?? 0, 0, MaxDurationSeconds);
        var (plan, hasOverride) = await ResolveScheduledAdsAsync(contentType, contentId, parentId);

        // Cấu hình do admin chỉnh (cache) → nếu chưa có thì dùng appsettings "Ads:Schedule".
        var settings = await GetScheduleSettingsAsync();

        // Override do admin chọn tay → luôn tôn trọng. Random chỉ áp cho content dùng global slots.
        if (hasOverride || settings.Mode != AdScheduleMode.Random)
            return plan;

        // Lịch random KHÔNG cache: mỗi lượt xem một lịch khác nhau (pool vẫn lấy từ cache global).
        return RandomAdPlanner.Plan(plan, duration, settings, Random.Shared);
    }

    /// <summary>Lịch cố định (global slots + override) — có cache. HasOverride = content có override riêng.</summary>
    private async Task<(ContentAdsDTO Plan, bool HasOverride)> ResolveScheduledAdsAsync(
        AdContentType contentType,
        Guid contentId,
        Guid? parentId = null)
    {
        // 1. Kiểm tra content-specific override trước
        //    Nếu content có override ở bất kỳ position nào → cache per (type, id)
        //    Nếu không → cache theo contentType (share giữa tất cả content cùng loại)

        var overrideList = (await _adRepo.GetOverridesByContentAsync(contentType, contentId)).ToList();

        // Tập phim không có override riêng → dùng override của CẢ BỘ (TvShow) nếu có.
        // Cache theo nguồn override (TvShow, parentId) để xóa override của bộ là xóa luôn cache các tập.
        var sourceType = contentType;
        var sourceId = contentId;
        if (overrideList.Count == 0 && contentType == AdContentType.Episode && parentId is { } showId && showId != Guid.Empty)
        {
            var showOverrides = (await _adRepo.GetOverridesByContentAsync(AdContentType.TvShow, showId)).ToList();
            if (showOverrides.Count > 0)
            {
                overrideList = showOverrides;
                sourceType = AdContentType.TvShow;
                sourceId = showId;
            }
        }

        if (overrideList.Count == 0)
        {
            // Không có override → dùng global cache (share giữa tất cả content cùng loại)
            var globalCacheKey = BuildGlobalCacheKey(contentType);
            var cached = await _cache.GetAsync<ContentAdsDTO>(globalCacheKey);
            if (cached != null)
            {
                // FIX: không mutate trực tiếp object lấy từ cache — nếu ICacheService
                // là in-memory và trả về cùng 1 reference cho mọi request, việc gán
                // cached.ContentId ở đây sẽ race giữa các user xem content khác nhau
                // cùng lúc (request A vừa set xong, request B ghi đè trước khi A kịp
                // return → A trả nhầm ContentId của B). Clone ra 1 object mới rồi patch.
                var patched = new ContentAdsDTO
                {
                    ContentType = cached.ContentType,
                    ContentId = contentId,
                    PreRoll = cached.PreRoll,
                    MidRoll = cached.MidRoll,
                    PostRoll = cached.PostRoll
                };
                return (patched, false);
            }

            var globalSlots = await LoadGlobalSlotsAsync(contentType);
            var result = BuildContentAdsDTO(contentType, contentId, globalSlots,
                new List<AdContentOverride>());

            // Cache với contentId = Guid.Empty (placeholder) để dùng chung
            var cacheResult = BuildContentAdsDTO(contentType, Guid.Empty, globalSlots,
                new List<AdContentOverride>());
            await _cache.SetAsync(globalCacheKey, cacheResult, AdCacheTtl);

            return (result, false);
        }
        else
        {
            // Có override → cache per nguồn override (tập hoặc cả bộ)
            var contentCacheKey = BuildContentCacheKey(sourceType, sourceId);
            var cached = await _cache.GetAsync<ContentAdsDTO>(contentCacheKey);
            if (cached != null) return (WithContent(cached, contentType, contentId), true);

            var globalSlots = await LoadGlobalSlotsAsync(contentType);
            var result = BuildContentAdsDTO(contentType, contentId, globalSlots, overrideList);

            await _cache.SetAsync(contentCacheKey, result, AdCacheTtl);
            return (result, true);
        }
    }

    // ── Schedule settings (Admin chỉnh được chế độ Fixed / Random) ───────────

    /// <summary>Giá trị admin đã lưu; chưa lưu lần nào thì dùng mặc định từ appsettings.</summary>
    public async Task<AdScheduleOptions> GetScheduleSettingsAsync()
    {
        var saved = await _cache.GetAsync<AdScheduleOptions>(ScheduleSettingsKey);
        return (saved ?? _scheduleOptions).Normalized();
    }

    public async Task<AdScheduleOptions> UpdateScheduleSettingsAsync(AdScheduleOptions dto)
    {
        var normalized = dto.Normalized();
        await _cache.SetAsync(ScheduleSettingsKey, normalized, ScheduleSettingsTtl);
        return normalized;
    }

    /// <summary>Xóa cấu hình admin đã lưu → quay về mặc định trong appsettings.</summary>
    public async Task<AdScheduleOptions> ResetScheduleSettingsAsync()
    {
        await _cache.RemoveAsync(ScheduleSettingsKey);
        return _scheduleOptions.Normalized();
    }

    /// <summary>
    /// Xem thử một lượt xem với cấu hình đang soạn (chưa lưu) hoặc đã lưu. Không ghi gì.
    ///   Random → chạy RandomAdPlanner trên kho quảng cáo (pool) hiện có.
    ///   Fixed  → trả đúng lịch cố định từ Global Slot.
    /// ContentType = null → "tất cả hình thức" (Phim lẻ + Tập phim).
    /// </summary>
    public async Task<SchedulePreviewResultDTO> PreviewScheduleAsync(SchedulePreviewRequestDTO request)
    {
        var options = (request.Options ?? await GetScheduleSettingsAsync()).Normalized();
        var duration = Math.Clamp(request.DurationSeconds, 0, MaxDurationSeconds);

        var types = request.ContentType is { } one
            ? new[] { one == AdContentType.TvShow ? AdContentType.Episode : one }
            : PlayableTypes;

        var merged = new ContentAdsDTO { ContentType = types[0], ContentId = Guid.Empty };
        foreach (var t in types)
        {
            var (fixedPlan, _) = await ResolveScheduledAdsAsync(t, Guid.Empty);
            merged.PreRoll.AddRange(fixedPlan.PreRoll);
            merged.MidRoll.AddRange(fixedPlan.MidRoll);
            merged.PostRoll.AddRange(fixedPlan.PostRoll);
        }
        // Slot áp "tất cả" xuất hiện ở cả hai loại → gộp theo SlotId
        merged.PreRoll = merged.PreRoll.DistinctBy(a => a.SlotId).ToList();
        merged.MidRoll = merged.MidRoll.DistinctBy(a => a.SlotId).OrderBy(a => a.MidRollOffsetSeconds).ToList();
        merged.PostRoll = merged.PostRoll.DistinctBy(a => a.SlotId).ToList();

        var pool = merged.PreRoll.Concat(merged.MidRoll).Concat(merged.PostRoll)
            .Where(a => !string.IsNullOrEmpty(a.VideoUrl))
            .DistinctBy(a => a.AdId)
            .Select(a => new PoolAdDTO { AdId = a.AdId, Title = a.Title, DurationSeconds = a.DurationSeconds })
            .ToList();

        var schedule = options.Mode == AdScheduleMode.Random
            ? RandomAdPlanner.Plan(merged, duration, options, Random.Shared)
            : merged;

        return new SchedulePreviewResultDTO
        {
            Mode = options.Mode,
            DurationSeconds = duration,
            Pool = pool,
            Schedule = schedule
        };
    }

    // ── Thứ tự hiển thị tiếp theo (Admin) ─────────────────────────────────────

    /// <summary>
    /// max(DisplayOrder) + 1 trong số các lịch CÙNG vị trí có thể phát chung với nhau; chưa có thì 0.
    ///   Override (contentType + contentId): chỉ xét override của content đó.
    ///   Global: phạm vi null = xét cả Movie / Episode / TvShow; Movie = Movie; Episode / TvShow = Episode + TvShow.
    /// </summary>
    public async Task<int> GetNextDisplayOrderAsync(
        AdPosition position, AdContentType? appliesTo, AdContentType? contentType, Guid? contentId)
    {
        IEnumerable<int> orders;

        if (contentType.HasValue && contentId.HasValue)
        {
            var overrides = await _adRepo.GetOverridesByContentAsync(contentType.Value, contentId.Value);
            orders = overrides.Where(o => o.Position == position).Select(o => o.DisplayOrder);
        }
        else
        {
            var scopes = appliesTo switch
            {
                null => new[] { AdContentType.Movie, AdContentType.Episode, AdContentType.TvShow },
                AdContentType.Movie => new[] { AdContentType.Movie },
                _ => new[] { AdContentType.Episode, AdContentType.TvShow }
            };

            var all = new List<GlobalAdSlot>();
            foreach (var s in scopes)
                all.AddRange(await _adRepo.GetActiveGlobalSlotsAsync(s));

            orders = all.Where(s => s.Position == position).Select(s => s.DisplayOrder);
        }

        var list = orders.ToList();
        return list.Count == 0 ? 0 : list.Max() + 1;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Global slot áp dụng cho 1 loại content. Tập phim (Episode) nhận thêm các slot phạm vi TvShow
    /// ("mọi tập của bộ"), vì player chỉ bao giờ hỏi quảng cáo theo Movie hoặc Episode.
    /// </summary>
    private async Task<List<GlobalAdSlot>> LoadGlobalSlotsAsync(AdContentType contentType)
    {
        var slots = (await _adRepo.GetActiveGlobalSlotsAsync(contentType)).ToList();
        if (contentType == AdContentType.Episode)
        {
            var known = slots.Select(s => s.Id).ToHashSet();
            slots.AddRange((await _adRepo.GetActiveGlobalSlotsAsync(AdContentType.TvShow))
                .Where(s => known.Add(s.Id)));
        }
        return slots;
    }

    private static ContentAdsDTO WithContent(ContentAdsDTO src, AdContentType type, Guid id) => new()
    {
        ContentType = type,
        ContentId = id,
        PreRoll = src.PreRoll,
        MidRoll = src.MidRoll,
        PostRoll = src.PostRoll
    };

    /// <summary>
    /// Merge global slots + content overrides.
    ///
    /// Rule:
    ///   - Với mỗi AdPosition: nếu content có override → dùng override, bỏ global.
    ///   - Nếu không có override ở position đó → dùng global slots.
    /// </summary>
    private static ContentAdsDTO BuildContentAdsDTO(
        AdContentType contentType,
        Guid contentId,
        List<GlobalAdSlot> globalSlots,
        List<AdContentOverride> overrides)
    {
        // Positions có override
        var overriddenPositions = overrides
            .Select(o => o.Position)
            .ToHashSet();

        // Effective ads per position:
        //   override positions → dùng override
        //   global positions không bị override → dùng global

        var preRoll = BuildPositionList(
            AdPosition.PreRoll,
            globalSlots, overrides, overriddenPositions);

        var midRoll = BuildPositionList(
            AdPosition.MidRoll,
            globalSlots, overrides, overriddenPositions,
            sortByOffset: true);

        var postRoll = BuildPositionList(
            AdPosition.PostRoll,
            globalSlots, overrides, overriddenPositions);

        return new ContentAdsDTO
        {
            ContentType = contentType,
            ContentId = contentId,
            PreRoll = preRoll,
            MidRoll = midRoll,
            PostRoll = postRoll
        };
    }

    private static List<AdPlaybackDTO> BuildPositionList(
        AdPosition position,
        List<GlobalAdSlot> globalSlots,
        List<AdContentOverride> overrides,
        HashSet<AdPosition> overriddenPositions,
        bool sortByOffset = false)
    {
        IEnumerable<(Advertisement Ad, Guid SlotId, int? Offset, int Order)> sources;

        if (overriddenPositions.Contains(position))
        {
            // Dùng override
            sources = overrides
                .Where(o => o.Position == position && o.Advertisement != null && o.Advertisement.IsActive)
                .Select(o => (o.Advertisement, o.Id, o.MidRollOffsetSeconds, o.DisplayOrder));
        }
        else
        {
            // Dùng global
            sources = globalSlots
                .Where(s => s.Position == position && s.Advertisement != null && s.Advertisement.IsActive)
                .Select(s => (s.Advertisement, s.Id, s.MidRollOffsetSeconds, s.DisplayOrder));
        }

        var ordered = sortByOffset
            ? sources.OrderBy(x => x.Offset).ThenBy(x => x.Order)
            : sources.OrderBy(x => x.Order);

        return ordered.Select(x => new AdPlaybackDTO
        {
            AdId = x.Ad.Id,
            Title = x.Ad.Title,
            VideoUrl = x.Ad.VideoUrl ?? string.Empty,
            BrandImageUrl = x.Ad.BrandImageUrl,
            DurationSeconds = x.Ad.DurationSeconds,
            SkipAfterSeconds = x.Ad.SkipAfterSeconds,
            ClickThroughUrl = x.Ad.ClickThroughUrl,
            SlotId = x.SlotId,
            Position = position,
            MidRollOffsetSeconds = x.Offset,
            DisplayOrder = x.Order
        }).ToList();
    }

    private static AdDTO MapToAdDTO(Advertisement ad) => new()
    {
        Id = ad.Id,
        Title = ad.Title,
        VideoUrl = ad.VideoUrl,
        BrandImageUrl = ad.BrandImageUrl,
        DurationSeconds = ad.DurationSeconds,
        SkipAfterSeconds = ad.SkipAfterSeconds,
        ClickThroughUrl = ad.ClickThroughUrl,
        IsActive = ad.IsActive,
        CreatedAt = ad.CreatedAt,
        UpdatedAt = ad.UpdatedAt,
        GlobalSlots = ad.GlobalSlots.Select(s => new GlobalSlotDTO
        {
            SlotId = s.Id,
            AppliesTo = s.AppliesTo,
            Position = s.Position,
            MidRollOffsetSeconds = s.MidRollOffsetSeconds,
            DisplayOrder = s.DisplayOrder,
            IsActive = s.IsActive
        }).ToList()
    };

    // Cache keys
    private static string BuildGlobalCacheKey(AdContentType contentType)
        => $"ads:global:{contentType}";

    private static string BuildContentCacheKey(AdContentType contentType, Guid contentId)
        => $"ads:content:{contentType}:{contentId}";

    /// <summary>
    /// Invalidate global cache theo AppliesTo scope của slot.
    /// AppliesTo = null → invalidate tất cả contentType.
    /// AppliesTo = X    → invalidate chỉ contentType X.
    /// </summary>
    private async Task InvalidateCacheForSlotAsync(AdContentType? appliesTo)
    {
        if (appliesTo == null)
        {
            // Slot áp tất cả → invalidate tất cả global cache keys
            var keys = Enum.GetValues<AdContentType>()
                .Select(BuildGlobalCacheKey)
                .ToArray();
            await _cache.RemoveManyAsync(keys);
        }
        else
        {
            await _cache.RemoveAsync(BuildGlobalCacheKey(appliesTo.Value));
            // Phạm vi TvShow được gộp vào cache của Episode
            if (appliesTo.Value == AdContentType.TvShow)
                await _cache.RemoveAsync(BuildGlobalCacheKey(AdContentType.Episode));
        }
    }

    /// <summary>Invalidate toàn bộ global cache (dùng khi ad bị edit/delete).</summary>
    private async Task InvalidateGlobalCacheAsync()
    {
        var keys = Enum.GetValues<AdContentType>()
            .Select(BuildGlobalCacheKey)
            .ToArray();
        await _cache.RemoveManyAsync(keys);
    }
}
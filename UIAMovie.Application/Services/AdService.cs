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
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

public class AdService : IAdService
{
    private readonly IAdRepository     _adRepo;
    private readonly ICloudinaryService _cloudinary;
    private readonly ICacheService     _cache;
    private readonly ILogger<AdService> _logger;

    private static readonly TimeSpan AdCacheTtl = TimeSpan.FromMinutes(5);

    public AdService(
        IAdRepository      adRepo,
        ICloudinaryService cloudinary,
        ICacheService      cache,
        ILogger<AdService> logger)
    {
        _adRepo     = adRepo;
        _cloudinary = cloudinary;
        _cache      = cache;
        _logger     = logger;
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

        string? videoUrl           = dto.VideoUrl;
        string? cloudinaryPublicId = null;

        if (dto.VideoFile != null)
        {
            videoUrl = await _cloudinary.UploadVideoAsync(dto.VideoFile, "uiamovie/ads");
            cloudinaryPublicId = Path.GetFileNameWithoutExtension(
                new Uri(videoUrl).AbsolutePath.Split('/').Last());
        }

        var ad = new Advertisement
        {
            Title              = dto.Title.Trim(),
            VideoUrl           = videoUrl,
            CloudinaryPublicId = cloudinaryPublicId,
            DurationSeconds    = dto.DurationSeconds,
            SkipAfterSeconds   = dto.SkipAfterSeconds,
            ClickThroughUrl    = dto.ClickThroughUrl?.Trim()
        };

        return await _adRepo.AddAsync(ad);
    }

    public async Task<bool> UpdateAdAsync(Guid id, UpdateAdDTO dto)
    {
        var ad = await _adRepo.GetByIdAsync(id);
        if (ad == null) return false;

        if (dto.Title    != null) ad.Title    = dto.Title.Trim();
        if (dto.IsActive != null) ad.IsActive = dto.IsActive.Value;
        if (dto.ClickThroughUrl != null)
            ad.ClickThroughUrl = string.IsNullOrWhiteSpace(dto.ClickThroughUrl)
                ? null
                : dto.ClickThroughUrl.Trim();

        if (dto.DurationSeconds  != null) ad.DurationSeconds  = dto.DurationSeconds.Value;
        if (dto.SkipAfterSeconds != null) ad.SkipAfterSeconds = dto.SkipAfterSeconds;

        if (dto.VideoFile != null)
        {
            if (!string.IsNullOrEmpty(ad.CloudinaryPublicId))
            {
                try { await _cloudinary.DeleteFileAsync(ad.CloudinaryPublicId); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[AdService] Không xóa được Cloudinary file {Id}", ad.CloudinaryPublicId);
                }
            }
            ad.VideoUrl           = await _cloudinary.UploadVideoAsync(dto.VideoFile, "uiamovie/ads");
            ad.CloudinaryPublicId = Path.GetFileNameWithoutExtension(
                                        new Uri(ad.VideoUrl).AbsolutePath.Split('/').Last());
        }
        else if (!string.IsNullOrWhiteSpace(dto.VideoUrl))
        {
            ad.VideoUrl           = dto.VideoUrl.Trim();
            ad.CloudinaryPublicId = null;
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

        // Invalidate trước khi xóa
        await InvalidateGlobalCacheAsync();

        if (!string.IsNullOrEmpty(ad.CloudinaryPublicId))
        {
            try { await _cloudinary.DeleteFileAsync(ad.CloudinaryPublicId); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AdService] Không xóa được Cloudinary file {Id}", ad.CloudinaryPublicId);
            }
        }

        await _adRepo.DeleteAsync(id);
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
            AdvertisementId      = adId,
            AppliesTo            = dto.AppliesTo,
            Position             = dto.Position,
            MidRollOffsetSeconds = dto.Position == AdPosition.MidRoll
                                       ? dto.MidRollOffsetSeconds
                                       : null,
            DisplayOrder         = dto.DisplayOrder
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

        if (dto.AppliesTo  != null) slot.AppliesTo  = dto.AppliesTo;
        if (dto.IsActive   != null) slot.IsActive    = dto.IsActive.Value;
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
            AdvertisementId      = adId,
            ContentType          = dto.ContentType,
            ContentId            = dto.ContentId,
            Position             = dto.Position,
            MidRollOffsetSeconds = dto.Position == AdPosition.MidRoll
                                       ? dto.MidRollOffsetSeconds
                                       : null,
            DisplayOrder         = dto.DisplayOrder
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
        Guid          contentId)
    {
        // 1. Kiểm tra content-specific override trước
        //    Nếu content có override ở bất kỳ position nào → cache per (type, id)
        //    Nếu không → cache theo contentType (share giữa tất cả content cùng loại)

        var overrides = await _adRepo.GetOverridesByContentAsync(contentType, contentId);
        var overrideList = overrides.ToList();

        if (overrideList.Count == 0)
        {
            // Không có override → dùng global cache (share giữa tất cả content cùng loại)
            var globalCacheKey = BuildGlobalCacheKey(contentType);
            var cached = await _cache.GetAsync<ContentAdsDTO>(globalCacheKey);
            if (cached != null)
            {
                // Patch ContentId cho đúng content (cache lưu contentId = default)
                cached.ContentId = contentId;
                return cached;
            }

            var globalSlots = await _adRepo.GetActiveGlobalSlotsAsync(contentType);
            var result = BuildContentAdsDTO(contentType, contentId, globalSlots.ToList(), new List<AdContentOverride>());

            // Cache với contentId = Guid.Empty (placeholder) để dùng chung
            var cacheResult = BuildContentAdsDTO(contentType, Guid.Empty, globalSlots.ToList(), new List<AdContentOverride>());
            await _cache.SetAsync(globalCacheKey, cacheResult, AdCacheTtl);

            return result;
        }
        else
        {
            // Có override → cache per (type, id)
            var contentCacheKey = BuildContentCacheKey(contentType, contentId);
            var cached = await _cache.GetAsync<ContentAdsDTO>(contentCacheKey);
            if (cached != null) return cached;

            var globalSlots = await _adRepo.GetActiveGlobalSlotsAsync(contentType);
            var result = BuildContentAdsDTO(contentType, contentId, globalSlots.ToList(), overrideList);

            await _cache.SetAsync(contentCacheKey, result, AdCacheTtl);
            return result;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Merge global slots + content overrides.
    ///
    /// Rule:
    ///   - Với mỗi AdPosition: nếu content có override → dùng override, bỏ global.
    ///   - Nếu không có override ở position đó → dùng global slots.
    /// </summary>
    private static ContentAdsDTO BuildContentAdsDTO(
        AdContentType            contentType,
        Guid                     contentId,
        List<GlobalAdSlot>       globalSlots,
        List<AdContentOverride>  overrides)
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
            ContentId   = contentId,
            PreRoll     = preRoll,
            MidRoll     = midRoll,
            PostRoll    = postRoll
        };
    }

    private static List<AdPlaybackDTO> BuildPositionList(
        AdPosition               position,
        List<GlobalAdSlot>       globalSlots,
        List<AdContentOverride>  overrides,
        HashSet<AdPosition>      overriddenPositions,
        bool                     sortByOffset = false)
    {
        IEnumerable<(Advertisement Ad, Guid SlotId, int? Offset, int Order)> sources;

        if (overriddenPositions.Contains(position))
        {
            // Dùng override
            sources = overrides
                .Where(o => o.Position == position && o.Advertisement != null)
                .Select(o => (o.Advertisement, o.Id, o.MidRollOffsetSeconds, o.DisplayOrder));
        }
        else
        {
            // Dùng global
            sources = globalSlots
                .Where(s => s.Position == position && s.Advertisement != null)
                .Select(s => (s.Advertisement, s.Id, s.MidRollOffsetSeconds, s.DisplayOrder));
        }

        var ordered = sortByOffset
            ? sources.OrderBy(x => x.Offset).ThenBy(x => x.Order)
            : sources.OrderBy(x => x.Order);

        return ordered.Select(x => new AdPlaybackDTO
        {
            AdId                 = x.Ad.Id,
            VideoUrl             = x.Ad.VideoUrl ?? string.Empty,
            DurationSeconds      = x.Ad.DurationSeconds,
            SkipAfterSeconds     = x.Ad.SkipAfterSeconds,
            ClickThroughUrl      = x.Ad.ClickThroughUrl,
            SlotId               = x.SlotId,
            Position             = position,
            MidRollOffsetSeconds = x.Offset,
            DisplayOrder         = x.Order
        }).ToList();
    }

    private static AdDTO MapToAdDTO(Advertisement ad) => new()
    {
        Id               = ad.Id,
        Title            = ad.Title,
        VideoUrl         = ad.VideoUrl,
        DurationSeconds  = ad.DurationSeconds,
        SkipAfterSeconds = ad.SkipAfterSeconds,
        ClickThroughUrl  = ad.ClickThroughUrl,
        IsActive         = ad.IsActive,
        CreatedAt        = ad.CreatedAt,
        UpdatedAt        = ad.UpdatedAt,
        GlobalSlots      = ad.GlobalSlots.Select(s => new GlobalSlotDTO
        {
            SlotId               = s.Id,
            AppliesTo            = s.AppliesTo,
            Position             = s.Position,
            MidRollOffsetSeconds = s.MidRollOffsetSeconds,
            DisplayOrder         = s.DisplayOrder,
            IsActive             = s.IsActive
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
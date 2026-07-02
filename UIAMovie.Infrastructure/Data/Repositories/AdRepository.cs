// UIAMovie.Infrastructure/Data/Repositories/AdRepository.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class AdRepository : IAdRepository
{
    private readonly MovieDbContext _context;

    public AdRepository(MovieDbContext context)
    {
        _context = context;
    }

    // ── Advertisement ─────────────────────────────────────────────────────────

    public async Task<Advertisement?> GetByIdAsync(Guid id)
    {
        return await _context.Advertisements
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<Advertisement?> GetByIdWithSlotsAsync(Guid id)
    {
        return await _context.Advertisements
            .AsNoTracking()
            .Include(a => a.GlobalSlots)
            .Include(a => a.Overrides)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<(IEnumerable<Advertisement> Items, int TotalCount)> GetPagedAsync(FilterAdsDTO filter)
    {
        var query = _context.Advertisements
            .AsNoTracking()
            .AsQueryable();

        if (filter.IsActive.HasValue)
            query = query.Where(a => a.IsActive == filter.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            query = query.Where(a => EF.Functions.ILike(a.Title, pattern));
        }

        query = query.OrderByDescending(a => a.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<Guid> AddAsync(Advertisement advertisement)
    {
        _context.Advertisements.Add(advertisement);
        await _context.SaveChangesAsync();
        return advertisement.Id;
    }

    public async Task UpdateAsync(Advertisement advertisement)
    {
        advertisement.UpdatedAt = DateTime.UtcNow;
        _context.Advertisements.Update(advertisement);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var ad = await _context.Advertisements.FindAsync(id);
        if (ad != null)
        {
            _context.Advertisements.Remove(ad);
            await _context.SaveChangesAsync();
        }
    }

    // ── GlobalAdSlot ──────────────────────────────────────────────────────────

    public async Task<GlobalAdSlot?> GetSlotByIdAsync(Guid slotId)
    {
        return await _context.GlobalAdSlots
            .AsNoTracking()
            .Include(s => s.Advertisement)
            .FirstOrDefaultAsync(s => s.Id == slotId);
    }

    public async Task<IEnumerable<GlobalAdSlot>> GetActiveGlobalSlotsAsync(AdContentType contentType)
    {
        // Lấy slots có AppliesTo = null (tất cả) HOẶC AppliesTo khớp contentType
        return await _context.GlobalAdSlots
            .AsNoTracking()
            .Include(s => s.Advertisement)
            .Where(s => s.IsActive
                     && s.Advertisement.IsActive
                     && (s.AppliesTo == null || s.AppliesTo == contentType))
            .OrderBy(s => s.Position)
            .ThenBy(s => s.MidRollOffsetSeconds)
            .ThenBy(s => s.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<GlobalAdSlot>> GetSlotsByAdAsync(Guid adId)
    {
        return await _context.GlobalAdSlots
            .AsNoTracking()
            .Where(s => s.AdvertisementId == adId)
            .OrderBy(s => s.Position)
            .ThenBy(s => s.DisplayOrder)
            .ToListAsync();
    }

    public async Task<Guid> AddSlotAsync(GlobalAdSlot slot)
    {
        _context.GlobalAdSlots.Add(slot);
        await _context.SaveChangesAsync();
        return slot.Id;
    }

    public async Task UpdateSlotAsync(GlobalAdSlot slot)
    {
        _context.GlobalAdSlots.Update(slot);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteSlotAsync(Guid slotId)
    {
        var slot = await _context.GlobalAdSlots.FindAsync(slotId);
        if (slot != null)
        {
            _context.GlobalAdSlots.Remove(slot);
            await _context.SaveChangesAsync();
        }
    }

    // ── AdContentOverride ─────────────────────────────────────────────────────

    public async Task<AdContentOverride?> GetOverrideByIdAsync(Guid overrideId)
    {
        return await _context.AdContentOverrides
            .AsNoTracking()
            .Include(o => o.Advertisement)
            .FirstOrDefaultAsync(o => o.Id == overrideId);
    }

    public async Task<IEnumerable<AdContentOverride>> GetOverridesByContentAsync(
        AdContentType contentType,
        Guid          contentId)
    {
        return await _context.AdContentOverrides
            .AsNoTracking()
            .Include(o => o.Advertisement)
            .Where(o => o.ContentType == contentType
                     && o.ContentId   == contentId
                     && o.IsActive
                     && o.Advertisement.IsActive)
            .OrderBy(o => o.Position)
            .ThenBy(o => o.MidRollOffsetSeconds)
            .ThenBy(o => o.DisplayOrder)
            .ToListAsync();
    }

    public async Task<Guid> AddOverrideAsync(AdContentOverride contentOverride)
    {
        _context.AdContentOverrides.Add(contentOverride);
        await _context.SaveChangesAsync();
        return contentOverride.Id;
    }

    public async Task UpdateOverrideAsync(AdContentOverride contentOverride)
    {
        _context.AdContentOverrides.Update(contentOverride);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteOverrideAsync(Guid overrideId)
    {
        var ov = await _context.AdContentOverrides.FindAsync(overrideId);
        if (ov != null)
        {
            _context.AdContentOverrides.Remove(ov);
            await _context.SaveChangesAsync();
        }
    }

    public async Task DeleteOverridesByContentAsync(AdContentType contentType, Guid contentId)
    {
        var overrides = await _context.AdContentOverrides
            .Where(o => o.ContentType == contentType && o.ContentId == contentId)
            .ToListAsync();

        if (overrides.Count > 0)
        {
            _context.AdContentOverrides.RemoveRange(overrides);
            await _context.SaveChangesAsync();
        }
    }
}
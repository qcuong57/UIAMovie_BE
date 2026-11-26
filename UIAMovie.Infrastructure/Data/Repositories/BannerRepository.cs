// UIAMovie.Infrastructure/Data/Repositories/BannerRepository.cs
using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class BannerRepository : IBannerRepository
{
    private readonly MovieDbContext _context;

    public BannerRepository(MovieDbContext context) => _context = context;

    public Task<Banner?> GetByIdAsync(Guid id)
        => _context.Banners.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);

    public async Task<(IEnumerable<Banner> Items, int TotalCount)> GetPagedAsync(FilterBannersDTO filter)
    {
        var query = _context.Banners.AsNoTracking().AsQueryable();

        if (filter.IsActive.HasValue)
            query = query.Where(b => b.IsActive == filter.IsActive.Value);
        if (filter.Placement.HasValue)
            query = query.Where(b => b.Placement == filter.Placement.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            query = query.Where(b => EF.Functions.ILike(b.Title, pattern));
        }

        query = query.OrderBy(b => b.Placement).ThenBy(b => b.DisplayOrder).ThenByDescending(b => b.CreatedAt);

        var total = await query.CountAsync();
        var page = Math.Max(1, filter.Page);
        var size = Math.Clamp(filter.PageSize, 1, 100);
        var items = await query.Skip((page - 1) * size).Take(size).ToListAsync();
        return (items, total);
    }

    public Task<List<Banner>> GetActiveByPlacementAsync(BannerPlacement placement)
        => _context.Banners.AsNoTracking()
            .Where(b => b.IsActive && b.Placement == placement)
            .OrderBy(b => b.DisplayOrder).ThenByDescending(b => b.CreatedAt)
            .ToListAsync();

    public async Task<Guid> AddAsync(Banner banner)
    {
        _context.Banners.Add(banner);
        await _context.SaveChangesAsync();
        return banner.Id;
    }

    public async Task UpdateAsync(Banner banner)
    {
        banner.UpdatedAt = DateTime.UtcNow;
        _context.Banners.Update(banner);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var b = await _context.Banners.FindAsync(id);
        if (b != null)
        {
            _context.Banners.Remove(b); // BannerEvents bị xoá theo (cascade)
            await _context.SaveChangesAsync();
        }
    }

    // ── Tracking ──────────────────────────────────────────────────────────────

    public async Task<HashSet<Guid>> GetExistingIdsAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        var found = await _context.Banners.AsNoTracking()
            .Where(b => list.Contains(b.Id)).Select(b => b.Id).ToListAsync();
        return found.ToHashSet();
    }

    public async Task AddEventsAsync(List<BannerEvent> events)
    {
        _context.BannerEvents.AddRange(events);
        await _context.SaveChangesAsync();
    }

    public async Task<List<BannerStatsRow>> GetStatsAsync(DateTime? fromUtc, DateTime? toUtcExclusive)
    {
        var ev = _context.BannerEvents.AsNoTracking().AsQueryable();
        var orders = _context.PaymentOrders.AsNoTracking().Where(o => o.BannerId != null);

        if (fromUtc.HasValue)
        {
            ev = ev.Where(e => e.CreatedAt >= fromUtc.Value);
            orders = orders.Where(o => o.CreatedAt >= fromUtc.Value);
        }
        if (toUtcExclusive.HasValue)
        {
            ev = ev.Where(e => e.CreatedAt < toUtcExclusive.Value);
            orders = orders.Where(o => o.CreatedAt < toUtcExclusive.Value);
        }

        var eventAgg = await ev
            .GroupBy(e => new { e.BannerId, e.Type })
            .Select(g => new { g.Key.BannerId, g.Key.Type, Count = g.Count() })
            .ToListAsync();

        // Đơn có tiền thật: Amount > 0 (đơn trial 0đ không tính doanh thu)
        var orderAgg = await orders
            .GroupBy(o => o.BannerId)
            .Select(g => new
            {
                BannerId = g.Key,
                Created = g.Count(),
                Paid = g.Count(o => o.Status == PaymentStatus.Success && o.Amount > 0),
                Revenue = g.Sum(o => o.Status == PaymentStatus.Success ? o.Amount : 0L)
            })
            .ToListAsync();

        var banners = await _context.Banners.AsNoTracking()
            .Select(b => new { b.Id, b.Title }).ToListAsync();

        return banners.Select(b =>
        {
            var o = orderAgg.FirstOrDefault(x => x.BannerId == b.Id);
            return new BannerStatsRow(
                b.Id,
                b.Title,
                eventAgg.FirstOrDefault(x => x.BannerId == b.Id && x.Type == BannerEventTypes.View)?.Count ?? 0,
                eventAgg.FirstOrDefault(x => x.BannerId == b.Id && x.Type == BannerEventTypes.Click)?.Count ?? 0,
                o?.Created ?? 0,
                o?.Paid ?? 0,
                o?.Revenue ?? 0L,
                eventAgg.FirstOrDefault(x => x.BannerId == b.Id && x.Type == BannerEventTypes.Dismiss)?.Count ?? 0,
                eventAgg.FirstOrDefault(x => x.BannerId == b.Id && x.Type == BannerEventTypes.Conversion)?.Count ?? 0);
        }).ToList();
    }
}
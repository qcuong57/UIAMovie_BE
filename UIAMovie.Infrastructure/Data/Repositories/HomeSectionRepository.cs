// UIAMovie.Infrastructure/Data/Repositories/HomeSectionRepository.cs
using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class HomeSectionRepository : Repository<HomeSection>, IHomeSectionRepository
{
    private readonly MovieDbContext _context;

    public HomeSectionRepository(MovieDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<List<HomeSection>> GetAllWithItemsAsync(bool onlyActive)
    {
        var q = _context.Set<HomeSection>().AsNoTracking().Include(s => s.Items).AsQueryable();
        if (onlyActive) q = q.Where(s => s.IsActive);
        return await q.OrderBy(s => s.SortOrder).ThenBy(s => s.CreatedAt).ToListAsync();
    }

    public async Task<HomeSection?> GetByIdWithItemsAsync(Guid id)
    {
        return await _context.Set<HomeSection>()
            .AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<HomeSection?> GetTrackedWithItemsAsync(Guid id)
    {
        return await _context.Set<HomeSection>()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<List<HomeSection>> GetTrackedByIdsAsync(IEnumerable<Guid> ids)
    {
        var list = ids.ToList();
        return await _context.Set<HomeSection>().Where(s => list.Contains(s.Id)).ToListAsync();
    }

    public async Task<int> GetMaxSortOrderAsync()
    {
        return await _context.Set<HomeSection>().AnyAsync()
            ? await _context.Set<HomeSection>().MaxAsync(s => s.SortOrder)
            : -1;
    }

    public void ReplaceItems(HomeSection section, IEnumerable<HomeSectionItem> newItems)
    {
        var set = _context.Set<HomeSectionItem>();
        set.RemoveRange(section.Items.ToList());
        // Add tường minh để EF đánh dấu Added (Guid key đã set sẵn sẽ bị hiểu nhầm là Modified nếu chỉ add vào navigation)
        set.AddRange(newItems);
    }
}
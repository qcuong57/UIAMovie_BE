// UIAMovie.Application/Interfaces/IRepositories/IHomeSectionRepository.cs
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Interfaces.IRepositories;

public interface IHomeSectionRepository : IRepository<HomeSection>
{
    /// <summary>AsNoTracking, kèm Items, sắp theo SortOrder.</summary>
    Task<List<HomeSection>> GetAllWithItemsAsync(bool onlyActive);

    /// <summary>AsNoTracking, kèm Items.</summary>
    Task<HomeSection?> GetByIdWithItemsAsync(Guid id);

    /// <summary>TRACKED, kèm Items — dùng cho update.</summary>
    Task<HomeSection?> GetTrackedWithItemsAsync(Guid id);

    Task<List<HomeSection>> GetTrackedByIdsAsync(IEnumerable<Guid> ids);

    Task<int> GetMaxSortOrderAsync();

    /// <summary>Thay toàn bộ item của section (xoá cũ, thêm mới). Chưa SaveChanges.</summary>
    void ReplaceItems(HomeSection section, IEnumerable<HomeSectionItem> newItems);
}
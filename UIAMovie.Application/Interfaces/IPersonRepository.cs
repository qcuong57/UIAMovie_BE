using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Interfaces;

// UIAMovie.Infrastructure/Data/Repositories/IPersonRepository.cs

using UIAMovie.Domain.Entities;

/// <summary>
/// Repository riêng cho Person — mirror IMovieRepository.
/// Lý do cần tách khỏi IRepository&lt;Person&gt; generic:
///   - SearchPagedAsync chạy ILIKE + Skip/Take trên DB (giống GetPagedAsync của Movie),
///     thay vì FindAsync() kéo hết bảng Person về RAM rồi lọc bằng LINQ to Objects.
///   - FindDuplicatesByNameAsync cần GROUP BY trên DB để tìm các Person trùng tên
///     (theo tên đã Trim + ToLower) — làm việc này trong C# sẽ phải load toàn bộ Person.
/// </summary>
public interface IPersonRepository : IRepository<Person>
{
    /// <summary>
    /// Autocomplete + trang danh sách Person. query = null/rỗng → trả tất cả (phân trang).
    /// ILIKE để không phân biệt hoa/thường, khớp cả tên tiếng Việt có dấu.
    /// </summary>
    Task<(IEnumerable<Person> Items, int TotalCount)> SearchPagedAsync(string? query, int page, int pageSize);

    /// <summary>
    /// Trả về Person kèm Images — dùng cho GetById chi tiết (tránh N+1 khi map ProfileImages).
    /// </summary>
    Task<Person?> GetByIdWithImagesAsync(Guid id);

    /// <summary>
    /// Nhóm các Person có cùng tên (đã Trim + ToLower), mỗi nhóm >= 2 người.
    /// Dùng cho công cụ "duplicates" bên admin — group theo tên chuẩn hóa NGAY TRÊN DB
    /// (GROUP BY + HAVING COUNT > 1), rồi mới load chi tiết từng nhóm.
    /// </summary>
    Task<IEnumerable<IGrouping<string, Person>>> FindDuplicatesByNameAsync();

    /// <summary>
    /// Có Person nào đang được gắn với ít nhất 1 MovieCast hoặc MovieDirector không.
    /// Dùng để chặn xóa cứng — PersonsController.Delete trả 400 nếu true.
    /// </summary>
    Task<bool> IsReferencedByAnyMovieAsync(Guid personId);
}
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Interfaces.IRepositories;

/// <summary>Tiêu chí tìm kiếm danh sách user (đã chuẩn hóa: PageNumber >= 1, PageSize trong giới hạn).</summary>
public sealed record UserSearchCriteria(
    string? Search,
    string? SubscriptionType, // "none"/"free" = không có gói đang hiệu lực
    string? Role,
    bool? IsActive,
    string? SortBy,
    bool SortDesc,
    int PageNumber,
    int PageSize);

/// <summary>Một trang user. Các entity chỉ chứa cột hiển thị (không có PasswordHash, TwoFaSecret).</summary>
public sealed record UserPage(IReadOnlyList<User> Items, int TotalCount);

public interface IUserRepository
{
    /// <summary>Lọc, đếm, sắp xếp và phân trang ngay trong DB.</summary>
    Task<UserPage> SearchAsync(UserSearchCriteria criteria, CancellationToken ct = default);

    /// <summary>
    /// True nếu user tồn tại và đang active (không bị khóa). Chỉ đọc đúng cột IsActive,
    /// không tải cả User (PasswordHash, TwoFaSecret...) nên dùng được ở đường nóng.
    /// </summary>
    Task<bool> IsActiveAsync(Guid userId, CancellationToken ct = default);
}
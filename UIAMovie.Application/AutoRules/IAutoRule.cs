using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Interfaces;

public enum AutoParamKind { None, Genre, Year, Country }

public record AutoRuleContext(string? Param, bool WantMovie, bool WantTv, int Limit);

/// <summary>
/// Một quy tắc tự động cho khối trang chủ. Thêm rule mới = tạo 1 class implement interface này
/// + đăng ký DI (services.AddScoped&lt;IAutoRule, XxxRule&gt;()). Không cần sửa service / constants / FE.
/// </summary>
public interface IAutoRule
{
    /// <summary>Key lưu DB (chữ thường, ví dụ "trending").</summary>
    string Key { get; }

    /// <summary>Tên hiển thị trong dropdown admin.</summary>
    string Label { get; }

    AutoParamKind ParamKind { get; }

    /// <summary>null = hợp lệ, ngược lại trả message lỗi.</summary>
    string? ValidateParam(string? param);

    /// <summary>Trả (card, key). Key càng lớn càng đứng trước — dùng để trộn movie + tvshow khi type = all.</summary>
    Task<List<(HomeContentCardDTO Card, double Key)>> ResolveAsync(AutoRuleContext ctx);
}
// UIAMovie.Application/Interfaces/IClock.cs

namespace UIAMovie.Application.Interfaces;

/// <summary>
/// Nguồn "hôm nay" duy nhất của hệ thống đếm view. ViewDate, thống kê và trending đều dùng chung
/// để không bị lệch múi giờ (người dùng ở Việt Nam, UTC+7).
/// </summary>
public interface IClock
{
    /// <summary>Ngày hiện tại theo giờ Việt Nam.</summary>
    DateOnly Today { get; }
}
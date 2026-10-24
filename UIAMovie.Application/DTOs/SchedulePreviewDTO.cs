// UIAMovie.Application/DTOs/SchedulePreviewDTO.cs
// "Xem thử" lịch quảng cáo từ admin. Không lưu gì.

using UIAMovie.Application.Options;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.DTOs;

public class SchedulePreviewRequestDTO
{
    /// <summary>Null = tất cả hình thức (Phim lẻ + Tập phim). TvShow được xem như Tập phim.</summary>
    public AdContentType? ContentType { get; set; }

    public int DurationSeconds { get; set; }

    /// <summary>Cấu hình đang soạn (chưa lưu). Null = dùng cấu hình đã lưu.</summary>
    public AdScheduleOptions? Options { get; set; }
}

/// <summary>Một quảng cáo nằm trong kho (pool) mà chế độ Random được phép bốc.</summary>
public class PoolAdDTO
{
    public Guid AdId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
}

public class SchedulePreviewResultDTO
{
    /// <summary>Chế độ đã dùng để dựng lịch mẫu.</summary>
    public AdScheduleMode Mode { get; set; }

    public int DurationSeconds { get; set; }

    /// <summary>Kho quảng cáo: các ad đang bật và đã gắn Global Slot (rỗng = sẽ không phát gì).</summary>
    public List<PoolAdDTO> Pool { get; set; } = new();

    public ContentAdsDTO Schedule { get; set; } = new();
}
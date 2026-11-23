// UIAMovie.Application/DTOs/PlaySessionDTOs.cs

using System.Text.Json.Serialization;

namespace UIAMovie.Application.DTOs;

public class StartPlaySessionDTO
{
    /// <summary>"Movie" hoặc "Episode" (TvShow không phát trực tiếp). Nhận cả chuỗi lẫn số.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UIAMovie.Domain.Entities.AdContentType ContentType { get; set; }

    public Guid ContentId { get; set; }

    /// <summary>Bắt buộc với Episode: id của bộ (TvShow).</summary>
    public Guid? ParentId { get; set; }
}

public class PlaySessionStartedDTO
{
    public string PlaySessionId { get; set; } = string.Empty;

    /// <summary>false = không có quảng cáo nào phải xem (Premium hoặc không có lịch).</summary>
    public bool RequiresAds { get; set; }

    /// <summary>Lịch quảng cáo đã CHỐT ở server. Mỗi ad có GrantRef để báo start/complete.</summary>
    public ContentAdsDTO Ads { get; set; } = new();

    public int ExpiresInSeconds { get; set; }

    /// <summary>Thời lượng (giây) server đã dùng để dựng lịch (lấy từ DB). Client dùng để quy đổi offset mid-roll sang thời lượng file thật.</summary>
    public int PlanDurationSeconds { get; set; }
}

public class AdProgressResultDTO
{
    public bool Ok { get; set; }

    /// <summary>TOO_EARLY | OUT_OF_ORDER | UNKNOWN_AD | NO_SESSION | FORBIDDEN</summary>
    public string? Code { get; set; }
}
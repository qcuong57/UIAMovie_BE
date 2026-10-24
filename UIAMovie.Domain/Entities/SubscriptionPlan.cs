// UIAMovie.Domain/Entities/SubscriptionPlan.cs

using System.Text.Json;

namespace UIAMovie.Domain.Entities;

/// <summary>
/// Gói subscription do admin cấu hình (thay cho danh sách hardcode trong PaymentService).
/// PaymentOrder đã snapshot tên/giá/số ngày lúc mua nên sửa gói không ảnh hưởng đơn cũ;
/// đơn tham chiếu gói theo chuỗi PlanId (không có FK) nên gói chỉ được tắt, không xóa.
/// </summary>
public class SubscriptionPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Slug duy nhất, vd "monthly_premium", "student_monthly", "trial_7d". Không đổi sau khi tạo.</summary>
    public string PlanId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Giá VND. 0 = gói miễn phí/dùng thử (không qua cổng thanh toán).</summary>
    public long PriceVnd { get; set; }

    /// <summary>Giá gốc để hiển thị gạch ngang (null = không có).</summary>
    public long? OriginalPriceVnd { get; set; }

    public int DurationDays { get; set; }

    public bool IsMostPopular { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>JSON array string, vd ["Full HD / 4K","Không quảng cáo"].</summary>
    public string FeaturesJson { get; set; } = "[]";

    /// <summary>Gói dùng thử: chỉ mua được khi user chưa có Premium còn hạn.</summary>
    public bool IsTrial { get; set; }

    /// <summary>Số lần tối đa một user được mua THÀNH CÔNG gói này (null = không giới hạn).</summary>
    public int? MaxPerUser { get; set; }

    /// <summary>CSV domain email được phép, vd "edu.vn,ac.vn". Null/rỗng = ai cũng mua được.</summary>
    public string? AllowedEmailDomains { get; set; }

    /// <summary>Khoảng thời gian bán (UTC). Null = không giới hạn đầu/cuối.</summary>
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<string> GetFeatures()
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(FeaturesJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
// UIAMovie.Application/DTOs/RevenueDTOs.cs
// Gồm: DTO báo cáo doanh thu + DTO danh sách đơn cho admin (AdminOrderDTO, AdminOrderFilterDTO).
// LƯU Ý: XÓA các class Revenue* / AdminOrder* cũ ở file khác (nếu còn), nếu không sẽ trùng tên.
// PaginatedDTO<T> giữ nguyên ở file hiện có.

namespace UIAMovie.Application.DTOs;

/// <summary>Toàn bộ số liệu thời gian tính theo giờ Việt Nam (GMT+7), tiền là VND thật.</summary>
public class RevenueSummaryDTO
{
    // Tổng toàn thời gian
    public long TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public int SuccessOrders { get; set; }
    public int PendingOrders { get; set; }   // đang chờ, CHƯA quá hạn
    public int FailedOrders { get; set; }    // cổng báo lỗi / sai số tiền
    public int ExpiredOrders { get; set; }   // bỏ dở, quá hạn (tách riêng khỏi Failed)

    /// <summary>Success / (Success + Failed + Expired). Không tính đơn Pending vì chưa có kết quả.</summary>
    public double SuccessRate { get; set; }
    public long AverageOrderValue { get; set; }

    // Hôm nay / tháng này / tháng trước
    public long TodayRevenue { get; set; }
    public int TodayOrders { get; set; }
    public long CurrentMonthRevenue { get; set; }
    public int CurrentMonthOrders { get; set; }
    public long LastMonthRevenue { get; set; }

    /// <summary>null khi tháng trước = 0 (không có gốc để so sánh) — FE hiển thị "—", không hiển thị 100% giả.</summary>
    public double? MonthOverMonthGrowth { get; set; }

    // Subscriber
    public int ActivePremiumUsers { get; set; }
    public int ExpiringIn7Days { get; set; }

    public DateTime GeneratedAt { get; set; }
}

public class RevenueDataPointDTO
{
    public string Label { get; set; } = string.Empty; // "2026-10" hoặc "2026-10-19"
    public long Revenue { get; set; }
    public int Orders { get; set; }
}

public class RevenueChartDTO
{
    public string GroupBy { get; set; } = "month";
    public List<RevenueDataPointDTO> DataPoints { get; set; } = new();
    public long TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
}

public class RevenueByPlanDTO
{
    public string PlanId { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long Revenue { get; set; }
    public int Orders { get; set; }
    public double RevenueShare { get; set; } // %
}

/// <summary>Bộ lọc export. From/To là NGÀY theo giờ VN, bao gồm cả ngày To. Mặc định chỉ xuất đơn Success.</summary>
public class RevenueExportFilterDTO
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Status { get; set; }
    public string? Provider { get; set; }
    public string? PlanId { get; set; }
}

public class RevenueExportDTO
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public long TotalRevenue { get; set; }
    public int SuccessOrders { get; set; }
    public List<RevenueByPlanDTO> ByPlan { get; set; } = new();
    public List<AdminOrderDTO> Orders { get; set; } = new();
    public DateTime GeneratedAtUtc { get; set; }
}

// ── Admin: danh sách đơn hàng ─────────────────────────────────────────────────

/// <summary>Một dòng đơn hàng hiển thị cho admin / export Excel. Thời gian là UTC.</summary>
public class AdminOrderDTO
{
    public Guid Id { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string PlanId { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string Provider { get; set; } = string.Empty;

    /// <summary>Trạng thái HIỆU LỰC: Pending quá hạn hiển thị là "expired".</summary>
    public string Status { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public string? GatewayTransId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}

/// <summary>Bộ lọc danh sách đơn. From/To là NGÀY theo giờ VN, gồm cả ngày To; lọc theo ngày tạo đơn.</summary>
public class AdminOrderFilterDTO
{
    public string? Status { get; set; }
    public string? PlanId { get; set; }
    public string? Provider { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>Tìm theo mã đơn hoặc email.</summary>
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
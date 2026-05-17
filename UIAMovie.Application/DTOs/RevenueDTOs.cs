// UIAMovie.Application/DTOs/RevenueDTOs.cs
namespace UIAMovie.Application.DTOs;

// ── Tổng quan doanh thu (Dashboard card) ─────────────────────────────────────

/// <summary>
/// Số liệu tổng hợp cho Admin Dashboard.
/// GET /api/payments/admin/revenue/summary
/// </summary>
public class RevenueSummaryDTO
{
    // ── Tổng cộng ──────────────────────────────────────────────────────────
    public long TotalRevenue          { get; set; }  // Tổng doanh thu VND (status=success)
    public int  TotalOrders           { get; set; }  // Tổng số đơn (mọi status)
    public int  SuccessOrders         { get; set; }  // Đơn thành công
    public int  FailedOrders          { get; set; }  // Đơn thất bại/expired
    public int  PendingOrders         { get; set; }  // Đơn đang chờ
    public double SuccessRate         { get; set; }  // % đơn thành công

    // ── Kỳ hiện tại (tháng này) ────────────────────────────────────────────
    public long   CurrentMonthRevenue { get; set; }
    public int    CurrentMonthOrders  { get; set; }

    // ── So với tháng trước ─────────────────────────────────────────────────
    public long   LastMonthRevenue    { get; set; }
    public double MonthOverMonthGrowth { get; set; } // %, dương = tăng trưởng

    // ── Subscriber ────────────────────────────────────────────────────────
    public int  ActivePremiumUsers    { get; set; }  // User còn hạn Premium
    public int  ExpiringIn7Days       { get; set; }  // Sắp hết hạn (cơ hội retention)
}

// ── Doanh thu theo ngày / tháng ───────────────────────────────────────────────

/// <summary>Một điểm dữ liệu trên biểu đồ doanh thu theo thời gian.</summary>
public class RevenueDataPointDTO
{
    public string Label   { get; set; } = string.Empty; // "2024-05" hoặc "2024-05-06"
    public long   Revenue { get; set; }
    public int    Orders  { get; set; }
}

/// <summary>
/// Doanh thu theo ngày trong một tháng, hoặc theo tháng trong một năm.
/// GET /api/payments/admin/revenue/chart?groupBy=month&year=2024
/// GET /api/payments/admin/revenue/chart?groupBy=day&year=2024&month=5
/// </summary>
public class RevenueChartDTO
{
    public string                    GroupBy    { get; set; } = string.Empty; // "day" | "month"
    public List<RevenueDataPointDTO> DataPoints { get; set; } = new();
    public long                      TotalRevenue { get; set; }
    public int                       TotalOrders  { get; set; }
}

// ── Doanh thu theo gói ───────────────────────────────────────────────────────

public class RevenueByPlanDTO
{
    public string PlanId      { get; set; } = string.Empty;
    public string PlanName    { get; set; } = string.Empty;
    public long   Revenue     { get; set; }
    public int    Orders      { get; set; }
    public double RevenueShare { get; set; } // % trên tổng
}

// ── Danh sách giao dịch (admin) ──────────────────────────────────────────────

/// <summary>
/// Filter cho admin khi xem danh sách đơn hàng.
/// GET /api/payments/admin/orders?status=success&planId=monthly_premium&page=1&pageSize=20
/// </summary>
public class AdminOrderFilterDTO
{
    public string?   Status    { get; set; }   // "pending" | "success" | "failed" | "expired"
    public string?   PlanId    { get; set; }
    public string?   Provider  { get; set; }
    public DateTime? From      { get; set; }
    public DateTime? To        { get; set; }
    public string?   Search    { get; set; }   // Tìm theo OrderCode hoặc Email user
    public int       Page      { get; set; } = 1;
    public int       PageSize  { get; set; } = 20;
}

public class AdminOrderDTO
{
    public Guid      Id             { get; set; }
    public string    OrderCode      { get; set; } = string.Empty;
    public Guid      UserId         { get; set; }
    public string    UserEmail      { get; set; } = string.Empty;
    public string    PlanName       { get; set; } = string.Empty;
    public long      Amount         { get; set; }
    public string    Provider       { get; set; } = string.Empty;
    public string    Status         { get; set; } = string.Empty;
    public string?   FailureReason  { get; set; }
    public string?   GatewayTransId { get; set; }
    public DateTime  CreatedAt      { get; set; }
    public DateTime? PaidAt         { get; set; }
}

public class PagedResultDTO<T>
{
    public List<T> Items      { get; set; } = new();
    public int     TotalCount { get; set; }
    public int     Page       { get; set; }
    public int     PageSize   { get; set; }
    public int     TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
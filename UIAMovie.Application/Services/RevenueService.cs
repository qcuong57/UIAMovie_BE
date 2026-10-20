// UIAMovie.Application/Services/RevenueService.cs

using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

public interface IRevenueService
{
    Task<RevenueSummaryDTO> GetSummaryAsync(CancellationToken ct = default);
    Task<RevenueChartDTO> GetChartAsync(string groupBy, int year, int? month, CancellationToken ct = default);

    /// <param name="from">Ngày bắt đầu (giờ VN, gồm cả ngày này).</param>
    /// <param name="to">Ngày kết thúc (giờ VN, gồm cả ngày này).</param>
    Task<List<RevenueByPlanDTO>> GetByPlanAsync(DateTime? from, DateTime? to, CancellationToken ct = default);

    Task<PaginatedDTO<AdminOrderDTO>> GetOrdersAsync(AdminOrderFilterDTO filter, CancellationToken ct = default);

    /// <summary>Dữ liệu cho file Excel. Ném ArgumentException nếu vượt giới hạn dòng.</summary>
    Task<RevenueExportDTO> GetExportAsync(RevenueExportFilterDTO filter, CancellationToken ct = default);
}

/// <summary>
/// Quy ước: ngày/tháng trong thống kê tính theo giờ Việt Nam (GMT+7); DB lưu UTC.
/// Ta đổi MỐC ngày VN sang UTC rồi để DB lọc, thay vì kéo toàn bộ dữ liệu lên RAM.
/// </summary>
internal static class VnTime
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(7); // VN không có DST

    public static DateTime ToVn(DateTime utc) => utc + Offset;

    /// <summary>Mốc 00:00 giờ VN (kiểu Unspecified) → thời điểm UTC tương ứng.</summary>
    public static DateTime VnToUtc(DateTime vnLocal) => DateTime.SpecifyKind(vnLocal - Offset, DateTimeKind.Utc);
}

public class RevenueService : IRevenueService
{
    private const int ExportMaxRows = 50_000;

    private readonly IPaymentOrderRepository _orders;
    private readonly IRepository<UserSubscription> _subRepo;

    public RevenueService(IPaymentOrderRepository orders, IRepository<UserSubscription> subRepo)
    {
        _orders  = orders;
        _subRepo = subRepo;
    }

    // ── Summary ───────────────────────────────────────────────────────────────

    public async Task<RevenueSummaryDTO> GetSummaryAsync(CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var nowVn  = VnTime.ToVn(nowUtc);

        var todayStartUtc     = VnTime.VnToUtc(nowVn.Date);
        var monthStartVn      = new DateTime(nowVn.Year, nowVn.Month, 1);
        var monthStartUtc     = VnTime.VnToUtc(monthStartVn);
        var lastMonthStartUtc = VnTime.VnToUtc(monthStartVn.AddMonths(-1));

        var totals = await _orders.GetStatusTotalsAsync(nowUtc, ct);
        var paid   = await _orders.GetPaidAsync(lastMonthStartUtc, null, ct); // chỉ ~2 tháng gần nhất

        // Đếm gói còn hạn: WHERE chạy ở DB, chỉ tải các dòng đang Premium
        var activeSubs = (await _subRepo.FindAsync(
            s => s.SubscriptionType == SubscriptionTypes.Premium && s.ExpiredAt > nowUtc, ct)).ToList();
        var in7Days = nowUtc.AddDays(7);

        StatusTotal T(string status) => totals.FirstOrDefault(t => t.Status == status) ?? new StatusTotal(status, 0, 0);

        var success = T(PaymentStatus.Success);
        var failed  = T(PaymentStatus.Failed);
        var expired = T(PaymentStatus.Expired);
        var pending = T(PaymentStatus.Pending);
        var cancelled = T(PaymentStatus.Cancelled);

        var thisMonth = paid.Where(p => p.PaidAtUtc >= monthStartUtc).ToList();
        var lastMonth = paid.Where(p => p.PaidAtUtc < monthStartUtc).ToList();
        var today     = thisMonth.Where(p => p.PaidAtUtc >= todayStartUtc).ToList();

        var thisMonthRevenue = thisMonth.Sum(p => p.Amount);
        var lastMonthRevenue = lastMonth.Sum(p => p.Amount);

        // Cancelled (bị thay bằng đơn mới) không phải thất bại/bỏ dở → không nằm trong mẫu số tỉ lệ thành công.
        var finalized = success.Count + failed.Count + expired.Count;

        return new RevenueSummaryDTO
        {
            TotalRevenue         = success.Amount,
            TotalOrders          = totals.Sum(t => t.Count),
            SuccessOrders        = success.Count,
            PendingOrders        = pending.Count,
            FailedOrders         = failed.Count,
            ExpiredOrders        = expired.Count,
            CancelledOrders      = cancelled.Count,
            SuccessRate          = finalized == 0 ? 0 : Math.Round((double)success.Count / finalized * 100, 1),
            AverageOrderValue    = success.Count == 0 ? 0 : success.Amount / success.Count,

            TodayRevenue         = today.Sum(p => p.Amount),
            TodayOrders          = today.Count,
            CurrentMonthRevenue  = thisMonthRevenue,
            CurrentMonthOrders   = thisMonth.Count,
            LastMonthRevenue     = lastMonthRevenue,
            MonthOverMonthGrowth = lastMonthRevenue == 0
                ? null
                : Math.Round((double)(thisMonthRevenue - lastMonthRevenue) / lastMonthRevenue * 100, 1),

            ActivePremiumUsers   = activeSubs.Count,
            ExpiringIn7Days      = activeSubs.Count(s => s.ExpiredAt <= in7Days),
            GeneratedAt          = nowUtc
        };
    }

    // ── Chart ─────────────────────────────────────────────────────────────────

    public async Task<RevenueChartDTO> GetChartAsync(string groupBy, int year, int? month, CancellationToken ct = default)
    {
        var byDay = groupBy == "day";
        if (byDay && month is null) throw new ArgumentException("Cần truyền month khi groupBy=day.");

        var startVn = byDay ? new DateTime(year, month!.Value, 1) : new DateTime(year, 1, 1);
        var endVn   = byDay ? startVn.AddMonths(1) : startVn.AddYears(1);

        var paid = await _orders.GetPaidAsync(VnTime.VnToUtc(startVn), VnTime.VnToUtc(endVn), ct);

        var buckets = paid
            .GroupBy(p => byDay ? VnTime.ToVn(p.PaidAtUtc).Day : VnTime.ToVn(p.PaidAtUtc).Month)
            .ToDictionary(g => g.Key, g => (Revenue: g.Sum(x => x.Amount), Orders: g.Count()));

        // Luôn trả đủ mốc (ngày/tháng không có đơn = 0) để biểu đồ không bị đứt
        var count  = byDay ? DateTime.DaysInMonth(year, month!.Value) : 12;
        var points = Enumerable.Range(1, count).Select(i =>
        {
            buckets.TryGetValue(i, out var b);
            return new RevenueDataPointDTO
            {
                Label   = byDay ? $"{year}-{month!.Value:D2}-{i:D2}" : $"{year}-{i:D2}",
                Revenue = b.Revenue,
                Orders  = b.Orders
            };
        }).ToList();

        return new RevenueChartDTO
        {
            GroupBy      = byDay ? "day" : "month",
            DataPoints   = points,
            TotalRevenue = points.Sum(p => p.Revenue),
            TotalOrders  = points.Sum(p => p.Orders)
        };
    }

    // ── By plan ───────────────────────────────────────────────────────────────

    public async Task<List<RevenueByPlanDTO>> GetByPlanAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        ValidateRange(from, to);
        var (fromUtc, toUtc) = ToUtcRange(from, to);

        var paid = await _orders.GetPaidAsync(fromUtc, toUtc, ct);
        return BuildByPlan(paid.Select(p => (p.PlanId, p.PlanName, p.Amount)));
    }

    private static List<RevenueByPlanDTO> BuildByPlan(IEnumerable<(string PlanId, string PlanName, long Amount)> rows)
    {
        var list  = rows.ToList();
        var total = list.Sum(r => r.Amount);

        return list
            .GroupBy(r => (r.PlanId, r.PlanName))
            .Select(g => new RevenueByPlanDTO
            {
                PlanId       = g.Key.PlanId,
                PlanName     = g.Key.PlanName,
                Revenue      = g.Sum(r => r.Amount),
                Orders       = g.Count(),
                RevenueShare = total == 0 ? 0 : Math.Round((double)g.Sum(r => r.Amount) / total * 100, 1)
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();
    }

    // ── Orders (filter + paging chạy ở DB) ────────────────────────────────────

    public async Task<PaginatedDTO<AdminOrderDTO>> GetOrdersAsync(AdminOrderFilterDTO filter, CancellationToken ct = default)
    {
        ValidateRange(filter.From, filter.To);
        var (fromUtc, toUtc) = ToUtcRange(filter.From, filter.To);

        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var page     = Math.Max(1, filter.Page);

        var query = new OrderQuery(NormalizeStatus(filter.Status), filter.PlanId, filter.Provider,
            fromUtc, toUtc, filter.Search, FilterByPaidAt: false, NowUtc: DateTime.UtcNow);

        var (items, total) = await _orders.SearchOrdersAsync(query, (page - 1) * pageSize, pageSize, ct);

        return new PaginatedDTO<AdminOrderDTO>
        {
            Items      = items,
            TotalCount = total,
            PageNumber = page,
            PageSize   = pageSize
        };
    }

    // ── Export ────────────────────────────────────────────────────────────────

    public async Task<RevenueExportDTO> GetExportAsync(RevenueExportFilterDTO filter, CancellationToken ct = default)
    {
        ValidateRange(filter.From, filter.To);
        var (fromUtc, toUtc) = ToUtcRange(filter.From, filter.To);

        var status = NormalizeStatus(filter.Status) ?? PaymentStatus.Success;

        // Đơn đã thanh toán → lọc theo NGÀY THANH TOÁN (mốc ghi nhận doanh thu); loại khác → ngày tạo.
        var query = new OrderQuery(status, filter.PlanId, filter.Provider, fromUtc, toUtc,
            Search: null, FilterByPaidAt: status == PaymentStatus.Success, NowUtc: DateTime.UtcNow);

        var orders = await _orders.ListOrdersAsync(query, ExportMaxRows + 1, ct);
        if (orders.Count > ExportMaxRows)
            throw new ArgumentException($"Quá {ExportMaxRows:N0} đơn, vui lòng thu hẹp khoảng thời gian.");

        orders = orders.OrderBy(o => o.PaidAt ?? o.CreatedAt).ToList(); // theo thứ tự thời gian cho kế toán

        var successRows = orders.Where(o => o.Status == PaymentStatus.Success).ToList();

        return new RevenueExportDTO
        {
            From          = filter.From?.Date,
            To            = filter.To?.Date,
            TotalRevenue  = successRows.Sum(o => o.Amount),
            SuccessOrders = successRows.Count,
            ByPlan        = BuildByPlan(successRows.Select(o => (o.PlanId, o.PlanName, o.Amount))),
            Orders        = orders,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Rỗng → null (không lọc). Giá trị lạ → 400 thay vì trả danh sách rỗng gây hiểu nhầm.</summary>
    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var s = status.Trim().ToLowerInvariant();
        if (!PaymentStatus.All.Contains(s))
            throw new ArgumentException($"status không hợp lệ: '{status.Trim()}'.");
        return s;
    }

    private static void ValidateRange(DateTime? from, DateTime? to)
    {
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            throw new ArgumentException("'from' phải nhỏ hơn hoặc bằng 'to'.");
    }

    /// <summary>Ngày VN gồm cả ngày cuối → [00:00 from, 00:00 (to + 1 ngày)) tính bằng UTC.</summary>
    private static (DateTime? FromUtc, DateTime? ToUtcExclusive) ToUtcRange(DateTime? from, DateTime? to) =>
        (from.HasValue ? VnTime.VnToUtc(from.Value.Date) : null,
         to.HasValue   ? VnTime.VnToUtc(to.Value.Date.AddDays(1)) : null);
}
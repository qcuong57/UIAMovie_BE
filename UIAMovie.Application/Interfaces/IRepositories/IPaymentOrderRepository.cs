// UIAMovie.Application/Interfaces/IRepositories/IPaymentOrderRepository.cs
// Repository chuyên biệt cho báo cáo + vòng đời đơn: để DB tự tính (WHERE/GROUP BY/SUM/LIMIT)
// thay vì kéo cả bảng PaymentOrder lên RAM qua IRepository<T>.FindAsync(_ => true).

using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Interfaces.IRepositories;

/// <summary>Số đơn + tổng tiền theo trạng thái HIỆU LỰC (Pending quá hạn tính là Expired).</summary>
public sealed record StatusTotal(string Status, int Count, long Amount);

/// <summary>Một đơn đã thanh toán — chỉ các cột cần cho thống kê (nhẹ hơn nhiều so với cả entity).</summary>
public sealed record PaidOrderPoint(DateTime PaidAtUtc, long Amount, string PlanId, string PlanName, string Provider);

/// <summary>Khoảng thời gian luôn là UTC, đầu đóng/cuối mở: [FromUtc, ToUtcExclusive).</summary>
public sealed record OrderQuery(
    string? Status,
    string? PlanId,
    string? Provider,
    DateTime? FromUtc,
    DateTime? ToUtcExclusive,
    string? Search,
    bool FilterByPaidAt,   // true: lọc theo ngày thanh toán (kế toán); false: theo ngày tạo đơn
    DateTime NowUtc);

public interface IPaymentOrderRepository
{
    Task<List<StatusTotal>> GetStatusTotalsAsync(DateTime nowUtc, CancellationToken ct = default);

    Task<List<PaidOrderPoint>> GetPaidAsync(DateTime? fromUtc, DateTime? toUtcExclusive, CancellationToken ct = default);

    Task<(List<AdminOrderDTO> Items, int Total)> SearchOrdersAsync(OrderQuery query, int skip, int take, CancellationToken ct = default);

    /// <summary>Lấy tối đa <paramref name="maxRows"/> đơn (không phân trang) — dùng cho export.</summary>
    Task<List<AdminOrderDTO>> ListOrdersAsync(OrderQuery query, int maxRows, CancellationToken ct = default);

    /// <summary>
    /// Chuyển Pending có ExpiredAt &lt;= cutoffUtc sang Expired bằng MỘT câu UPDATE có điều kiện
    /// (WHERE status = pending) nên không ghi đè đơn vừa được IPN xác nhận. Trả về số dòng đổi.
    /// </summary>
    Task<int> ExpireStalePendingAsync(DateTime cutoffUtc, CancellationToken ct = default);
}
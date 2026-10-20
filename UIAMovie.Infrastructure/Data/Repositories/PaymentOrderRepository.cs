// UIAMovie.Infrastructure/Data/Repositories/PaymentOrderRepository.cs

using Microsoft.EntityFrameworkCore;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class PaymentOrderRepository : IPaymentOrderRepository
{
    private readonly MovieDbContext _db;

    public PaymentOrderRepository(MovieDbContext db) => _db = db;

    public async Task<List<StatusTotal>> GetStatusTotalsAsync(DateTime nowUtc, CancellationToken ct = default) =>
        await _db.Set<PaymentOrder>().AsNoTracking()
            // Pending đã quá hạn được gộp vào Expired ngay trong SQL (CASE WHEN)
            .GroupBy(o => o.Status == PaymentStatus.Pending && o.ExpiredAt <= nowUtc
                ? PaymentStatus.Expired
                : o.Status)
            .Select(g => new StatusTotal(g.Key, g.Count(), g.Sum(o => o.Amount)))
            .ToListAsync(ct);

    public async Task<List<PaidOrderPoint>> GetPaidAsync(
        DateTime? fromUtc, DateTime? toUtcExclusive, CancellationToken ct = default)
    {
        var q = _db.Set<PaymentOrder>().AsNoTracking()
            .Where(o => o.Status == PaymentStatus.Success && o.PaidAt != null);

        if (fromUtc.HasValue) q = q.Where(o => o.PaidAt >= fromUtc.Value);
        if (toUtcExclusive.HasValue) q = q.Where(o => o.PaidAt < toUtcExclusive.Value);

        return await q
            .Select(o => new PaidOrderPoint(o.PaidAt!.Value, o.Amount, o.PlanId, o.PlanName, o.Provider))
            .ToListAsync(ct);
    }

    public async Task<(List<AdminOrderDTO> Items, int Total)> SearchOrdersAsync(
        OrderQuery query, int skip, int take, CancellationToken ct = default)
    {
        var q = BuildQuery(query);
        var total = await q.CountAsync(ct);
        var items = await q.Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }

    public async Task<List<AdminOrderDTO>> ListOrdersAsync(OrderQuery query, int maxRows,
        CancellationToken ct = default) =>
        await BuildQuery(query).Take(maxRows).ToListAsync(ct);

    public async Task<int> ExpireStalePendingAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        await _db.Set<PaymentOrder>()
            .Where(o => o.Status == PaymentStatus.Pending && o.ExpiredAt <= cutoffUtc)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, PaymentStatus.Expired)
                .SetProperty(o => o.FailureReason, "Hết hạn thanh toán"), ct);

    // ── Query dùng chung cho list + export ────────────────────────────────────

    private IQueryable<AdminOrderDTO> BuildQuery(OrderQuery f)
    {
        var now = f.NowUtc;

        var joined =
            from o in _db.Set<PaymentOrder>().AsNoTracking()
            join u in _db.Set<User>().AsNoTracking() on o.UserId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            select new { o, Email = u.Email };

        if (!string.IsNullOrWhiteSpace(f.Status))
        {
            var s = f.Status.Trim();
            if (s == PaymentStatus.Pending)
                joined = joined.Where(x => x.o.Status == PaymentStatus.Pending && x.o.ExpiredAt > now);
            else if (s == PaymentStatus.Expired)
                joined = joined.Where(x => x.o.Status == PaymentStatus.Expired
                                           || (x.o.Status == PaymentStatus.Pending && x.o.ExpiredAt <= now));
            else
                joined = joined.Where(x => x.o.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(f.PlanId))
        {
            var planId = f.PlanId.Trim();
            joined = joined.Where(x => x.o.PlanId == planId);
        }

        if (!string.IsNullOrWhiteSpace(f.Provider))
        {
            var provider = f.Provider.Trim();
            joined = joined.Where(x => x.o.Provider == provider);
        }

        var from = f.FromUtc;
        var to = f.ToUtcExclusive;
        if (f.FilterByPaidAt)
        {
            if (from.HasValue) joined = joined.Where(x => x.o.PaidAt >= from.Value);
            if (to.HasValue) joined = joined.Where(x => x.o.PaidAt < to.Value);
        }
        else
        {
            if (from.HasValue) joined = joined.Where(x => x.o.CreatedAt >= from.Value);
            if (to.HasValue) joined = joined.Where(x => x.o.CreatedAt < to.Value);
        }

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var kw = f.Search.Trim().ToLower();
            joined = joined.Where(x => x.o.OrderCode.ToLower().Contains(kw)
                                       || (x.Email != null && x.Email.ToLower().Contains(kw)));
        }

        return joined
            .OrderByDescending(x => x.o.CreatedAt)
            .Select(x => new AdminOrderDTO
            {
                Id = x.o.Id,
                OrderCode = x.o.OrderCode,
                UserId = x.o.UserId,
                UserEmail = x.Email ?? string.Empty,
                PlanId = x.o.PlanId,
                PlanName = x.o.PlanName,
                Amount = x.o.Amount,
                Provider = x.o.Provider,
                Status = x.o.Status == PaymentStatus.Pending && x.o.ExpiredAt <= now
                    ? PaymentStatus.Expired
                    : x.o.Status,
                FailureReason = x.o.FailureReason,
                GatewayTransId = x.o.GatewayTransId,
                CreatedAt = x.o.CreatedAt,
                PaidAt = x.o.PaidAt
            });
    }
}
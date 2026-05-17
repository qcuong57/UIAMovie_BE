// UIAMovie.Application/Services/PaymentService.cs

using System.Text.Json;
using Microsoft.Extensions.Logging;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Services.Payment;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Data.Repositories;

namespace UIAMovie.Application.Services;

// ── Interface ─────────────────────────────────────────────────────────────────

public interface IPaymentService
{
    /// <summary>Trả danh sách gói subscription để user chọn.</summary>
    IEnumerable<SubscriptionPlanDTO> GetSubscriptionPlans();

    /// <summary>
    /// Bước 1 trong flow: User chọn gói → tạo PaymentOrder → trả về URL redirect sang VNPay.
    /// </summary>
    /// <param name="backendBaseUrl">Base URL của backend (VD: "https://api.yourdomain.com").
    /// Dùng để tự build ReturnUrl gửi cho VNPay, không cần cấu hình cứng trong appsettings.</param>
    Task<PaymentOrderResponseDTO> CreateOrderAsync(Guid userId, CreatePaymentOrderDTO dto, string ipAddress, string backendBaseUrl);

    /// <summary>
    /// Bước 2: VNPay IPN callback (server-to-server).
    /// Xác thực chữ ký → kiểm tra amount → cập nhật trạng thái → nâng gói nếu thành công → invalidate cache.
    /// </summary>
    Task<bool> HandleVnpayIpnAsync(VnpayIpnDTO ipn);

    /// <summary>Lịch sử thanh toán của user.</summary>
    Task<IEnumerable<PaymentHistoryDTO>> GetPaymentHistoryAsync(Guid userId);

    /// <summary>Trạng thái subscription hiện tại của user.</summary>
    Task<SubscriptionStatusDTO> GetSubscriptionStatusAsync(Guid userId);

    /// <summary>[Admin] Cấp hoặc gia hạn Premium cho user bất kỳ.</summary>
    Task AdminGrantSubscriptionAsync(AdminGrantSubscriptionDTO dto);

    /// <summary>[Admin] Thu hồi Premium của user.</summary>
    Task AdminRevokeSubscriptionAsync(Guid userId);
    
    Task<RevenueSummaryDTO> GetRevenueSummaryAsync();
    Task<RevenueChartDTO> GetRevenueChartAsync(string groupBy, int year, int? month = null);
    Task<List<RevenueByPlanDTO>> GetRevenueByPlanAsync(DateTime? from = null, DateTime? to = null);
    Task<PagedResultDTO<AdminOrderDTO>> GetAdminOrdersAsync(AdminOrderFilterDTO filter);
}



// ── Admin DTOs ────────────────────────────────────────────────────────────────

public class AdminGrantSubscriptionDTO
{
    public Guid     UserId    { get; set; }
    public string   PlanId    { get; set; } = string.Empty;  // "monthly_premium" | "yearly_premium"
    public DateTime ExpiredAt { get; set; }
}

// ── Implementation ────────────────────────────────────────────────────────────

public class PaymentService : IPaymentService
{
    private readonly IRepository<PaymentOrder>      _orderRepo;
    private readonly IRepository<UserSubscription>  _subRepo;
    private readonly IRepository<User>              _userRepo;
    private readonly IVnpayPaymentService           _vnpay;
    private readonly ICacheService                  _cache;
    private readonly ILogger<PaymentService>        _logger;

    private const string USER_CACHE_KEY = "user:id:{0}";
    private const string SUB_CACHE_KEY  = "subscription:{0}";

    private static readonly List<SubscriptionPlanDTO> Plans = new()
    {
        new SubscriptionPlanDTO
        {
            PlanId        = "monthly_premium",
            Name          = "Premium Tháng",
            Description   = "Xem không giới hạn trong 30 ngày",
            PriceVnd      = 69_000,
            PriceDisplay  = "69.000đ/tháng",
            DurationDays  = 30,
            IsMostPopular = false,
            Features      = new() { "Full HD / 4K", "Không quảng cáo", "Tải về xem offline" }
        },
        new SubscriptionPlanDTO
        {
            PlanId        = "yearly_premium",
            Name          = "Premium Năm",
            Description   = "Tiết kiệm hơn 28% so với gói tháng",
            PriceVnd      = 599_000,
            PriceDisplay  = "599.000đ/năm",
            DurationDays  = 365,
            IsMostPopular = true,
            Features      = new() { "Full HD / 4K", "Không quảng cáo", "Tải về xem offline", "Ưu tiên hỗ trợ" }
        }
    };

    public PaymentService(
        IRepository<PaymentOrder>     orderRepo,
        IRepository<UserSubscription> subRepo,
        IRepository<User>             userRepo,
        IVnpayPaymentService          vnpay,
        ICacheService                 cache,
        ILogger<PaymentService>       logger)
    {
        _orderRepo = orderRepo;
        _subRepo   = subRepo;
        _userRepo  = userRepo;
        _vnpay     = vnpay;
        _cache     = cache;
        _logger    = logger;
    }

    // ── Plans ─────────────────────────────────────────────────────────────────

    public IEnumerable<SubscriptionPlanDTO> GetSubscriptionPlans() => Plans;

    // ── Bước 1: Tạo order → redirect URL ─────────────────────────────────────

    /// <summary>
    /// Flow: User chọn gói Premium
    ///   → POST /api/payments/create-order
    ///   → Tạo PaymentOrder (status = pending)
    ///   → Gọi VNPay lấy paymentUrl
    ///   → Trả về PaymentOrderResponseDTO (frontend redirect)
    /// </summary>
    public async Task<PaymentOrderResponseDTO> CreateOrderAsync(
        Guid userId, CreatePaymentOrderDTO dto, string ipAddress, string backendBaseUrl)
    {
        // 1. Validate plan
        var plan = Plans.FirstOrDefault(p => p.PlanId == dto.PlanId)
                   ?? throw new ArgumentException($"Gói '{dto.PlanId}' không tồn tại.");

        // 2. Validate provider — chỉ hỗ trợ vnpay
        var provider = dto.PaymentProvider.ToLower();
        if (provider != "vnpay")
            throw new ArgumentException("Provider phải là 'vnpay'.");

        // 3. Tạo order code duy nhất: ORD-20240506-A3F7
        var orderCode = GenerateOrderCode();
        var expiredAt = DateTime.UtcNow.AddMinutes(15);

        // 4. Lưu PaymentOrder vào DB
        var order = new PaymentOrder
        {
            OrderCode    = orderCode,
            UserId       = userId,
            PlanId       = plan.PlanId,
            PlanName     = plan.Name,
            DurationDays = plan.DurationDays,
            Amount       = plan.PriceVnd,
            Provider     = provider,
            Status       = PaymentStatus.Pending,
            ExpiredAt    = expiredAt
        };

        await _orderRepo.AddAsync(order);
        await _orderRepo.SaveChangesAsync();

        _logger.LogInformation(
            "Created PaymentOrder {OrderCode} for user {UserId} — plan={PlanId} provider={Provider}",
            orderCode, userId, plan.PlanId, provider);

        // 5. Gọi VNPay lấy URL
        var orderInfo       = $"Nâng cấp {plan.Name} - {orderCode}";
        // Build returnUrl từ backendBaseUrl thay vì cấu hình cứng trong appsettings
        var backendReturnUrl = $"{backendBaseUrl.TrimEnd('/')}/api/payments/vnpay/return";
        string paymentUrl;

        try
        {
            paymentUrl = _vnpay.CreatePaymentUrl(orderCode, plan.PriceVnd, orderInfo, ipAddress, backendReturnUrl);
        }
        catch (Exception ex)
        {
            order.Status        = PaymentStatus.Failed;
            order.FailureReason = $"VNPay gateway error: {ex.Message}";
            _orderRepo.Update(order);
            await _orderRepo.SaveChangesAsync();

            _logger.LogError(ex, "Failed to get payment URL from VNPay for order {OrderCode}", orderCode);
            throw;
        }

        return new PaymentOrderResponseDTO
        {
            OrderId    = order.Id,
            OrderCode  = orderCode,
            PaymentUrl = paymentUrl,
            Amount     = plan.PriceVnd,
            Provider   = provider,
            ExpiredAt  = expiredAt
        };
    }

    // ── Bước 2: VNPay IPN ────────────────────────────────────────────────────

    /// <summary>
    /// Flow sau khi user thực hiện thanh toán qua VNPay:
    ///   IPN Callback (server-to-server) → POST /api/payments/vnpay/ipn
    ///   → Xác thực chữ ký HMAC-SHA512
    ///   → Kiểm tra amount khớp (VNPay gửi *100)
    ///   → Tìm order theo vnp_TxnRef (= OrderCode)
    ///   → Cập nhật status (success / failed)
    ///   → Nếu success: nâng SubscriptionType → Invalidate User Cache
    ///
    /// LƯU Ý: ReturnUrl (GET /api/payments/vnpay/return) chỉ dùng để redirect
    /// browser về frontend — KHÔNG xử lý upgrade subscription ở đó.
    /// </summary>
    public async Task<bool> HandleVnpayIpnAsync(VnpayIpnDTO ipn)
    {
        _logger.LogInformation("VNPay IPN received: txnRef={TxnRef} responseCode={Code}",
            ipn.vnp_TxnRef, ipn.vnp_ResponseCode);

        // 1. Xác thực chữ ký
        if (!_vnpay.VerifySignature(ipn))
        {
            _logger.LogWarning("VNPay IPN signature invalid for order {TxnRef}", ipn.vnp_TxnRef);
            return false;
        }

        // 2. Tìm order
        var orders = await _orderRepo.FindAsync(o => o.OrderCode == ipn.vnp_TxnRef);
        var order  = orders.FirstOrDefault();
        if (order == null)
        {
            _logger.LogWarning("VNPay IPN: order not found {TxnRef}", ipn.vnp_TxnRef);
            return false;
        }

        // 3. Idempotent — tránh xử lý 2 lần
        if (order.Status != PaymentStatus.Pending)
        {
            _logger.LogInformation("VNPay IPN: order {OrderCode} already processed ({Status})",
                order.OrderCode, order.Status);
            return true;
        }

        // 4. Kiểm tra amount khớp
        // VNPay gửi vnp_Amount đã nhân 100 so với VND thật
        var actualAmount = ipn.vnp_Amount / 100;
        if (actualAmount != order.Amount)
        {
            _logger.LogWarning(
                "VNPay IPN amount mismatch: expected={Expected} got={Got} (raw={Raw}) for order {OrderCode}",
                order.Amount, actualAmount, ipn.vnp_Amount, order.OrderCode);

            order.Status        = PaymentStatus.Failed;
            order.FailureReason = $"Amount mismatch: expected {order.Amount}, got {actualAmount}";
            order.RawCallback   = JsonSerializer.Serialize(ipn);
            _orderRepo.Update(order);
            await _orderRepo.SaveChangesAsync();
            return false;
        }

        // 5. Lưu raw callback
        order.RawCallback   = JsonSerializer.Serialize(ipn);
        order.GatewayTransId = ipn.vnp_TransactionNo;

        // 6. Cập nhật trạng thái
        bool success = ipn.vnp_ResponseCode == "00"; // VNPay: "00" = thành công
        if (success)
        {
            order.Status  = PaymentStatus.Success;
            order.PaidAt  = DateTime.UtcNow;
            await UpgradeSubscriptionAsync(order);
        }
        else
        {
            order.Status        = PaymentStatus.Failed;
            order.FailureReason = $"VNPay ResponseCode={ipn.vnp_ResponseCode}";
        }

        _orderRepo.Update(order);
        await _orderRepo.SaveChangesAsync();

        _logger.LogInformation("VNPay IPN processed: order={OrderCode} success={Success}",
            order.OrderCode, success);

        return true;
    }

    // ── Subscription upgrade ──────────────────────────────────────────────────

    /// <summary>
    /// Thanh toán thành công:
    ///   1. Upsert UserSubscription (cộng thêm ngày nếu đang còn Premium)
    ///   2. Cập nhật User.SubscriptionType = "Premium"
    ///   3. Invalidate user cache
    /// </summary>
    private async Task UpgradeSubscriptionAsync(PaymentOrder order)
    {
        var subs = await _subRepo.FindAsync(s => s.UserId == order.UserId);
        var sub  = subs.FirstOrDefault();

        var now = DateTime.UtcNow;
        // Nếu đang còn Premium → cộng tiếp từ ngày hết hạn, không từ hôm nay
        var baseDate = (sub?.ExpiredAt != null && sub.ExpiredAt > now)
            ? sub.ExpiredAt.Value
            : now;

        var newExpiry = baseDate.AddDays(order.DurationDays);

        if (sub == null)
        {
            sub = new UserSubscription
            {
                UserId              = order.UserId,
                SubscriptionType    = SubscriptionTypes.Premium,
                StartedAt           = now,
                ExpiredAt           = newExpiry,
                LastPaymentOrderId  = order.Id,
                UpdatedAt           = now
            };
            await _subRepo.AddAsync(sub);
        }
        else
        {
            sub.SubscriptionType   = SubscriptionTypes.Premium;
            sub.StartedAt          = sub.StartedAt ?? now;
            sub.ExpiredAt          = newExpiry;
            sub.LastPaymentOrderId = order.Id;
            sub.UpdatedAt          = now;
            _subRepo.Update(sub);
        }

        await _subRepo.SaveChangesAsync();

        // Đồng bộ User.SubscriptionType
        var user = await _userRepo.GetByIdAsync(order.UserId);
        if (user != null)
        {
            user.SubscriptionType = SubscriptionTypes.Premium;
            user.UpdatedAt        = now;
            _userRepo.Update(user);
            await _userRepo.SaveChangesAsync();
        }

        // Invalidate cache
        await InvalidateUserCacheAsync(order.UserId, user?.Email);

        _logger.LogInformation(
            "Upgraded user {UserId} to Premium until {Expiry} (order={OrderCode})",
            order.UserId, newExpiry, order.OrderCode);
    }

    // ── Query helpers ─────────────────────────────────────────────────────────

    public async Task<IEnumerable<PaymentHistoryDTO>> GetPaymentHistoryAsync(Guid userId)
    {
        var orders = await _orderRepo.FindAsync(o => o.UserId == userId);

        return orders
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new PaymentHistoryDTO
            {
                Id            = o.Id,
                OrderCode     = o.OrderCode,
                PlanName      = o.PlanName,
                Amount        = o.Amount,
                Provider      = o.Provider,
                Status        = o.Status,
                FailureReason = o.FailureReason,
                CreatedAt     = o.CreatedAt,
                PaidAt        = o.PaidAt
            });
    }

    public async Task<SubscriptionStatusDTO> GetSubscriptionStatusAsync(Guid userId)
    {
        var cacheKey = string.Format(SUB_CACHE_KEY, userId);
        return await _cache.GetOrSetAsync(cacheKey, async () =>
        {
            var subs = await _subRepo.FindAsync(s => s.UserId == userId);
            var sub  = subs.FirstOrDefault();

            var now       = DateTime.UtcNow;
            var isPremium = sub?.SubscriptionType == SubscriptionTypes.Premium
                            && sub.ExpiredAt > now;

            int? daysLeft = isPremium
                ? (int?)Math.Ceiling((sub!.ExpiredAt!.Value - now).TotalDays)
                : null;

            return new SubscriptionStatusDTO
            {
                SubscriptionType = isPremium ? SubscriptionTypes.Premium : SubscriptionTypes.Free,
                IsPremium        = isPremium,
                ExpiredAt        = sub?.ExpiredAt,
                DaysRemaining    = daysLeft,
                IsExpiringSoon   = isPremium && daysLeft <= 7
            };
        }, TimeSpan.FromMinutes(10));
    }


    // ── Admin: Cấp / Thu hồi Premium ─────────────────────────────────────────

    /// <summary>
    /// [Admin] Cấp hoặc gia hạn Premium thủ công.
    /// Upsert UserSubscription + đồng bộ User.SubscriptionType + invalidate cache.
    /// </summary>
    public async Task AdminGrantSubscriptionAsync(AdminGrantSubscriptionDTO dto)
    {
        var plan = Plans.FirstOrDefault(p => p.PlanId == dto.PlanId)
                   ?? throw new ArgumentException($"Gói '{dto.PlanId}' không tồn tại.");

        var user = await _userRepo.GetByIdAsync(dto.UserId)
                   ?? throw new ArgumentException("Không tìm thấy user.");

        var now  = DateTime.UtcNow;
        var subs = await _subRepo.FindAsync(s => s.UserId == dto.UserId);
        var sub  = subs.FirstOrDefault();

        if (sub == null)
        {
            sub = new UserSubscription
            {
                UserId           = dto.UserId,
                SubscriptionType = SubscriptionTypes.Premium,
                StartedAt        = now,
                ExpiredAt        = dto.ExpiredAt.ToUniversalTime(),
                UpdatedAt        = now
            };
            await _subRepo.AddAsync(sub);
        }
        else
        {
            sub.SubscriptionType = SubscriptionTypes.Premium;
            sub.ExpiredAt        = dto.ExpiredAt.ToUniversalTime();
            sub.UpdatedAt        = now;
            _subRepo.Update(sub);
        }

        await _subRepo.SaveChangesAsync();

        // Đồng bộ User.SubscriptionType
        user.SubscriptionType = SubscriptionTypes.Premium;
        user.UpdatedAt        = now;
        _userRepo.Update(user);
        await _userRepo.SaveChangesAsync();

        await InvalidateUserCacheAsync(dto.UserId, user.Email);

        _logger.LogInformation(
            "[Admin] Granted Premium to user {UserId} until {Expiry} (plan={PlanId})",
            dto.UserId, dto.ExpiredAt, dto.PlanId);
    }

    /// <summary>
    /// [Admin] Thu hồi Premium của user:
    ///   - Xóa UserSubscription nếu có
    ///   - Reset User.SubscriptionType = null
    ///   - Invalidate cache
    /// </summary>
    public async Task AdminRevokeSubscriptionAsync(Guid userId)
    {
        var user = await _userRepo.GetByIdAsync(userId)
                   ?? throw new ArgumentException("Không tìm thấy user.");

        var subs = await _subRepo.FindAsync(s => s.UserId == userId);
        foreach (var sub in subs)
            _subRepo.Remove(sub);

        if (subs.Any())
            await _subRepo.SaveChangesAsync();

        // Reset subscription trên User — dùng "free" vì SubscriptionType là non-nullable string
        user.SubscriptionType = SubscriptionTypes.Free;
        user.UpdatedAt        = DateTime.UtcNow;
        _userRepo.Update(user);
        await _userRepo.SaveChangesAsync();

        await InvalidateUserCacheAsync(userId, user.Email);

        _logger.LogInformation("[Admin] Revoked Premium from user {UserId}", userId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>ORD-20240506-A3F7 — ngắn, dễ tra cứu, đủ random.</summary>
    private static string GenerateOrderCode()
    {
        var date   = DateTime.UtcNow.ToString("yyyyMMdd");
        var suffix = Guid.NewGuid().ToString("N")[..4].ToUpper();
        return $"ORD-{date}-{suffix}";
    }

    private async Task InvalidateUserCacheAsync(Guid userId, string? email)
    {
        await _cache.RemoveAsync(string.Format(USER_CACHE_KEY, userId));
        await _cache.RemoveAsync(string.Format(SUB_CACHE_KEY, userId));

        // Xóa thêm cache key của SubscriptionChecker (dùng key riêng ":isPremium")
        // để tránh trường hợp user vừa thanh toán xong nhưng content vẫn bị chặn
        await _cache.RemoveAsync($"subscription:{userId}:isPremium");

        if (!string.IsNullOrEmpty(email))
            await _cache.RemoveAsync($"user:email:{email.ToLower()}");
    }
    
     /// <summary>
    /// Tổng hợp số liệu cho Dashboard admin:
    ///   - Tổng doanh thu / đơn hàng toàn thời gian
    ///   - Doanh thu tháng này vs tháng trước + MoM growth
    ///   - Số user Premium đang active + sắp hết hạn
    /// </summary>
    public async Task<RevenueSummaryDTO> GetRevenueSummaryAsync()
    {
        var allOrders = await _orderRepo.FindAsync(_ => true);
        var allSubs   = await _subRepo.FindAsync(_ => true);
 
        var now       = DateTime.UtcNow;
        var thisMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastMonth = thisMonth.AddMonths(-1);
 
        var successOrders  = allOrders.Where(o => o.Status == PaymentStatus.Success).ToList();
        var failedOrders   = allOrders.Where(o => o.Status is PaymentStatus.Failed or PaymentStatus.Expired).ToList();
        var pendingOrders  = allOrders.Where(o => o.Status == PaymentStatus.Pending).ToList();
 
        var totalRevenue   = successOrders.Sum(o => o.Amount);
        var totalOrders    = allOrders.Count();
 
        var currentMonthOrders  = successOrders.Where(o => o.PaidAt >= thisMonth).ToList();
        var lastMonthOrders     = successOrders.Where(o => o.PaidAt >= lastMonth && o.PaidAt < thisMonth).ToList();
 
        var currentMonthRevenue = currentMonthOrders.Sum(o => o.Amount);
        var lastMonthRevenue    = lastMonthOrders.Sum(o => o.Amount);
 
        double momGrowth = lastMonthRevenue == 0
            ? (currentMonthRevenue > 0 ? 100.0 : 0.0)
            : Math.Round((double)(currentMonthRevenue - lastMonthRevenue) / lastMonthRevenue * 100, 1);
 
        double successRate = totalOrders == 0
            ? 0
            : Math.Round((double)successOrders.Count / totalOrders * 100, 1);
 
        var activePremium   = allSubs.Count(s => s.SubscriptionType == SubscriptionTypes.Premium && s.ExpiredAt > now);
        var expiringIn7Days = allSubs.Count(s =>
            s.SubscriptionType == SubscriptionTypes.Premium
            && s.ExpiredAt > now
            && s.ExpiredAt <= now.AddDays(7));
 
        return new RevenueSummaryDTO
        {
            TotalRevenue           = totalRevenue,
            TotalOrders            = totalOrders,
            SuccessOrders          = successOrders.Count,
            FailedOrders           = failedOrders.Count,
            PendingOrders          = pendingOrders.Count,
            SuccessRate            = successRate,
            CurrentMonthRevenue    = currentMonthRevenue,
            CurrentMonthOrders     = currentMonthOrders.Count,
            LastMonthRevenue       = lastMonthRevenue,
            MonthOverMonthGrowth   = momGrowth,
            ActivePremiumUsers     = activePremium,
            ExpiringIn7Days        = expiringIn7Days
        };
    }
 
    // ── [Admin] Revenue Chart ─────────────────────────────────────────────────
 
    /// <summary>
    /// Dữ liệu biểu đồ doanh thu:
    ///   groupBy="month" + year=2024        → 12 điểm Jan–Dec 2024
    ///   groupBy="day"   + year=2024&month=5 → từng ngày trong tháng 5/2024
    /// </summary>
    public async Task<RevenueChartDTO> GetRevenueChartAsync(string groupBy, int year, int? month = null)
    {
        var successOrders = await _orderRepo.FindAsync(
            o => o.Status == PaymentStatus.Success && o.PaidAt.HasValue);
 
        var filtered = successOrders.Where(o => o.PaidAt!.Value.Year == year);
 
        List<RevenueDataPointDTO> dataPoints;
 
        if (groupBy == "day" && month.HasValue)
        {
            // Từng ngày trong tháng
            filtered = filtered.Where(o => o.PaidAt!.Value.Month == month.Value);
            var daysInMonth = DateTime.DaysInMonth(year, month.Value);
 
            dataPoints = Enumerable.Range(1, daysInMonth).Select(day =>
            {
                var dayOrders = filtered.Where(o => o.PaidAt!.Value.Day == day).ToList();
                return new RevenueDataPointDTO
                {
                    Label   = $"{year}-{month.Value:D2}-{day:D2}",
                    Revenue = dayOrders.Sum(o => o.Amount),
                    Orders  = dayOrders.Count
                };
            }).ToList();
        }
        else
        {
            // Từng tháng trong năm (default)
            dataPoints = Enumerable.Range(1, 12).Select(m =>
            {
                var monthOrders = filtered.Where(o => o.PaidAt!.Value.Month == m).ToList();
                return new RevenueDataPointDTO
                {
                    Label   = $"{year}-{m:D2}",
                    Revenue = monthOrders.Sum(o => o.Amount),
                    Orders  = monthOrders.Count
                };
            }).ToList();
        }
 
        return new RevenueChartDTO
        {
            GroupBy      = groupBy,
            DataPoints   = dataPoints,
            TotalRevenue = dataPoints.Sum(d => d.Revenue),
            TotalOrders  = dataPoints.Sum(d => d.Orders)
        };
    }
 
    // ── [Admin] Revenue By Plan ───────────────────────────────────────────────
 
    /// <summary>
    /// Phân tích doanh thu theo từng gói (monthly vs yearly).
    /// from/to = null → toàn thời gian.
    /// </summary>
    public async Task<List<RevenueByPlanDTO>> GetRevenueByPlanAsync(DateTime? from = null, DateTime? to = null)
    {
        var successOrders = await _orderRepo.FindAsync(o => o.Status == PaymentStatus.Success);
 
        if (from.HasValue) successOrders = successOrders.Where(o => o.PaidAt >= from.Value);
        if (to.HasValue)   successOrders = successOrders.Where(o => o.PaidAt <= to.Value);
 
        var total = successOrders.Sum(o => o.Amount);
 
        var grouped = successOrders
            .GroupBy(o => new { o.PlanId, o.PlanName })
            .Select(g => new RevenueByPlanDTO
            {
                PlanId       = g.Key.PlanId,
                PlanName     = g.Key.PlanName,
                Revenue      = g.Sum(o => o.Amount),
                Orders       = g.Count(),
                RevenueShare = total == 0 ? 0 : Math.Round((double)g.Sum(o => o.Amount) / total * 100, 1)
            })
            .OrderByDescending(x => x.Revenue)
            .ToList();
 
        return grouped;
    }
 
    // ── [Admin] Danh sách đơn hàng có filter ─────────────────────────────────
 
    /// <summary>
    /// Lấy danh sách đơn hàng với filter linh hoạt:
    ///   - Lọc theo status, planId, provider, khoảng ngày
    ///   - Search theo OrderCode hoặc email user
    ///   - Phân trang
    ///
    /// LƯU Ý: join với User để lấy email — nếu IRepository không hỗ trợ
    /// Include/Join, hãy inject DbContext trực tiếp hoặc tạo IPaymentRepository riêng.
    /// Code dưới đây minh hoạ với FindAsync + in-memory join (phù hợp dataset nhỏ).
    /// Production nên dùng IQueryable để push filter xuống DB.
    /// </summary>
    public async Task<PagedResultDTO<AdminOrderDTO>> GetAdminOrdersAsync(AdminOrderFilterDTO filter)
    {
        var allOrders = await _orderRepo.FindAsync(_ => true);
        var allUsers  = await _userRepo.FindAsync(_ => true);
 
        // Build email lookup: userId → email
        var emailMap = allUsers.ToDictionary(u => u.Id, u => u.Email ?? string.Empty);
 
        // Apply filters
        var query = allOrders.AsEnumerable();
 
        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(o => o.Status == filter.Status);
 
        if (!string.IsNullOrWhiteSpace(filter.PlanId))
            query = query.Where(o => o.PlanId == filter.PlanId);
 
        if (!string.IsNullOrWhiteSpace(filter.Provider))
            query = query.Where(o => o.Provider == filter.Provider);
 
        if (filter.From.HasValue)
            query = query.Where(o => o.CreatedAt >= filter.From.Value);
 
        if (filter.To.HasValue)
            query = query.Where(o => o.CreatedAt <= filter.To.Value);
 
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var kw = filter.Search.Trim().ToLower();
            query = query.Where(o =>
                o.OrderCode.ToLower().Contains(kw)
                || (emailMap.TryGetValue(o.UserId, out var email) && email.ToLower().Contains(kw)));
        }
 
        // Count trước khi phân trang
        var ordered    = query.OrderByDescending(o => o.CreatedAt).ToList();
        var totalCount = ordered.Count;
 
        // Phân trang
        var pageSize = Math.Max(1, Math.Min(filter.PageSize, 100));
        var page     = Math.Max(1, filter.Page);
        var items    = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new AdminOrderDTO
            {
                Id             = o.Id,
                OrderCode      = o.OrderCode,
                UserId         = o.UserId,
                UserEmail      = emailMap.TryGetValue(o.UserId, out var em) ? em : string.Empty,
                PlanName       = o.PlanName,
                Amount         = o.Amount,
                Provider       = o.Provider,
                Status         = o.Status,
                FailureReason  = o.FailureReason,
                GatewayTransId = o.GatewayTransId,
                CreatedAt      = o.CreatedAt,
                PaidAt         = o.PaidAt
            })
            .ToList();
 
        return new PagedResultDTO<AdminOrderDTO>
        {
            Items      = items,
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize
        };
    }
}
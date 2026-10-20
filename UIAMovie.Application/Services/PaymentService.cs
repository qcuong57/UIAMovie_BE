// UIAMovie.Application/Services/PaymentService.cs

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UIAMovie.Application.Caching;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Services;

// ── Interface ─────────────────────────────────────────────────────────────────

/// <summary>
/// Kết quả xử lý callback/IPN của cổng thanh toán (dùng chung cho VNPay và SePay).
/// Controller ánh xạ sang mã phản hồi riêng của từng cổng.
/// </summary>
public enum PaymentCallbackResult
{
    Confirmed,        // VNPay 00 / SePay 200
    OrderNotFound,    // 01
    AlreadyConfirmed, // 02
    InvalidAmount,    // 04
    InvalidSignature, // 97 (VNPay: sai chữ ký; SePay: sai X-Secret-Key)
    Busy,             // 99 — đang xử lý ở request khác, cổng sẽ gửi lại
    Ignored           // callback hợp lệ nhưng không cần xử lý (VD SePay TRANSACTION_VOID)
}

public interface IPaymentService
{
    /// <summary>Trả danh sách gói subscription để user chọn.</summary>
    IEnumerable<SubscriptionPlanDTO> GetSubscriptionPlans();

    /// <summary>
    /// Bước 1 trong flow: User chọn gói → tạo PaymentOrder → trả về thông tin chuyển sang cổng thanh toán
    /// (VNPay: URL redirect; SePay: URL + các field để FE POST form).
    /// </summary>
    /// <param name="backendBaseUrl">Base URL của backend (VD: "https://api.yourdomain.com").
    /// Dùng để tự build ReturnUrl/Callback URL gửi cho cổng, không cần cấu hình cứng trong appsettings.</param>
    Task<PaymentOrderResponseDTO> CreateOrderAsync(Guid userId, CreatePaymentOrderDTO dto, string ipAddress, string backendBaseUrl);

    /// <summary>
    /// Bước 2: VNPay IPN / Return callback.
    /// Xác thực chữ ký → khóa theo đơn → kiểm tra amount → cập nhật trạng thái → nâng gói nếu thành công → invalidate cache.
    /// </summary>
    /// <param name="ipn">Giá trị đã bind, dùng để đọc dữ liệu.</param>
    /// <param name="rawQuery">Toàn bộ query string gốc (chỉ các tham số vnp_*), dùng để xác thực chữ ký.</param>
    Task<PaymentCallbackResult> HandleVnpayIpnAsync(VnpayIpnDTO ipn, IReadOnlyDictionary<string, string> rawQuery);

    /// <summary>
    /// Bước 2 (SePay): IPN POST JSON từ SePay. Xác thực header X-Secret-Key → lọc loại thông báo
    /// → dùng chung lõi xác nhận với VNPay (khóa, idempotent, kiểm tra amount, nâng gói).
    /// KHÔNG gọi hàm này từ return URL của trình duyệt.
    /// </summary>
    /// <param name="secretHeader">Giá trị header X-Secret-Key của request.</param>
    Task<PaymentCallbackResult> HandleSepayIpnAsync(SepayIpnDTO ipn, string? secretHeader);

    /// <summary>Lịch sử thanh toán của user.</summary>
    Task<IEnumerable<PaymentHistoryDTO>> GetPaymentHistoryAsync(Guid userId);

    /// <summary>Trạng thái subscription hiện tại của user.</summary>
    Task<SubscriptionStatusDTO> GetSubscriptionStatusAsync(Guid userId);

    /// <summary>[Admin] Cấp hoặc gia hạn Premium cho user bất kỳ.</summary>
    Task AdminGrantSubscriptionAsync(AdminGrantSubscriptionDTO dto);

    /// <summary>[Admin] Thu hồi Premium của user.</summary>
    Task AdminRevokeSubscriptionAsync(Guid userId);

    /// <summary>User tự hủy Premium của chính mình.</summary>
    Task CancelMySubscriptionAsync(Guid userId);

    /// <summary>
    /// Chuyển đơn Pending quá hạn (+ thời gian ân hạn) sang Expired. Gọi định kỳ bởi PaymentExpiryWorker.
    /// Báo cáo doanh thu / danh sách đơn admin nằm ở IRevenueService. Trả về số đơn đã đổi.
    /// </summary>
    Task<int> ExpireStaleOrdersAsync(CancellationToken ct = default);
}

// ── Admin DTOs ────────────────────────────────────────────────────────────────

public class AdminGrantSubscriptionDTO
{
    public Guid     UserId    { get; set; }
    public string   PlanId    { get; set; } = string.Empty;  // "monthly_premium" | "yearly_premium"
    public DateTime ExpiredAt { get; set; }
}

// ── Implementation ────────────────────────────────────────────────────────────

/// <summary>Giá trị lưu ở PaymentOrder.Provider.</summary>
public static class PaymentProviders
{
    public const string Vnpay = "vnpay";
    public const string Sepay = "sepay";
}

public class PaymentService : IPaymentService
{
    private readonly IRepository<PaymentOrder>      _orderRepo;
    private readonly IPaymentOrderRepository        _paymentOrders;
    private readonly IRepository<UserSubscription>  _subRepo;
    private readonly IRepository<User>              _userRepo;
    private readonly IVnpayPaymentService           _vnpay;
    private readonly ISepayPaymentService           _sepay;
    private readonly ICacheService                  _cache;
    private readonly ILogger<PaymentService>        _logger;

    private const string SUB_CACHE_KEY  = "subscription:{0}";

    /// <summary>Ân hạn sau ExpiredAt để IPN đến muộn vẫn được xử lý trước khi đơn bị chốt Expired.</summary>
    private static readonly TimeSpan ExpiryGrace = TimeSpan.FromMinutes(30);

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
        IPaymentOrderRepository       paymentOrders,
        IRepository<UserSubscription> subRepo,
        IRepository<User>             userRepo,
        IVnpayPaymentService          vnpay,
        ISepayPaymentService          sepay,
        ICacheService                 cache,
        ILogger<PaymentService>       logger)
    {
        _orderRepo = orderRepo;
        _paymentOrders = paymentOrders;
        _subRepo   = subRepo;
        _userRepo  = userRepo;
        _vnpay     = vnpay;
        _sepay     = sepay;
        _cache     = cache;
        _logger    = logger;
    }

    // ── Plans ─────────────────────────────────────────────────────────────────

    public IEnumerable<SubscriptionPlanDTO> GetSubscriptionPlans() => Plans;

    // ── Bước 1: Tạo order → redirect URL ─────────────────────────────────────

    public async Task<PaymentOrderResponseDTO> CreateOrderAsync(
        Guid userId, CreatePaymentOrderDTO dto, string ipAddress, string backendBaseUrl)
    {
        // 1. Validate plan
        var plan = Plans.FirstOrDefault(p => p.PlanId == dto.PlanId)
                   ?? throw new ArgumentException($"Gói '{dto.PlanId}' không tồn tại.");

        // 2. Validate provider — hỗ trợ vnpay và sepay
        var provider = dto.PaymentProvider.ToLowerInvariant();
        if (provider is not (PaymentProviders.Vnpay or PaymentProviders.Sepay))
            throw new ArgumentException("Provider phải là 'vnpay' hoặc 'sepay'.");

        // 3. Mỗi user chỉ giữ MỘT đơn đang chờ: các đơn Pending còn hạn trước đó được chốt Expired
        //    (lưu cùng lần SaveChanges với đơn mới). Nếu người dùng thật ra đã trả đơn cũ thì callback
        //    đến sau vẫn được chấp nhận (xem "thanh toán muộn" trong ConfirmOrderAsync).
        var now = DateTime.UtcNow;
        await SupersedePendingOrdersAsync(userId, now);

        // 4. Tạo order code duy nhất: ORD-20240506-9F3A07C1
        var orderCode = await GenerateUniqueOrderCodeAsync();
        var expiredAt = now.AddMinutes(15);

        // 5. Lưu PaymentOrder vào DB
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

        // 6. Gọi cổng thanh toán lấy thông tin chuyển hướng
        var orderInfo = $"Nâng cấp {plan.Name} - {orderCode}";
        var baseUrl   = backendBaseUrl.TrimEnd('/');

        var response = new PaymentOrderResponseDTO
        {
            OrderId   = order.Id,
            OrderCode = orderCode,
            Amount    = plan.PriceVnd,
            Provider  = provider,
            ExpiredAt = expiredAt
        };

        try
        {
            if (provider == PaymentProviders.Vnpay)
            {
                response.PaymentUrl = _vnpay.CreatePaymentUrl(
                    orderCode, plan.PriceVnd, orderInfo, ipAddress, $"{baseUrl}/api/payments/vnpay/return");
            }
            else
            {
                // Dùng path segment thay vì query để các URL được ký không chứa '&'
                var returnBase = $"{baseUrl}/api/payments/sepay/return/{orderCode}";
                var form = _sepay.CreateCheckoutForm(
                    orderCode, plan.PriceVnd, orderInfo,
                    successUrl: $"{returnBase}/success",
                    errorUrl:   $"{returnBase}/error",
                    cancelUrl:  $"{returnBase}/cancel");

                response.PaymentUrl    = form.Action;
                response.PaymentMethod = "form_post";
                response.FormFields    = form.Fields;
            }
        }
        catch (Exception ex)
        {
            order.Status        = PaymentStatus.Failed;
            order.FailureReason = $"{provider} gateway error: {ex.Message}";
            _orderRepo.Update(order);
            await _orderRepo.SaveChangesAsync();

            _logger.LogError(ex, "Failed to create payment for order {OrderCode} (provider={Provider})",
                orderCode, provider);
            throw;
        }

        return response;
    }

    // ── Bước 2: Callback từ cổng thanh toán ──────────────────────────────────

    /// <summary>
    /// Cả IPN (VNPay → server) và Return (trình duyệt → server) đều gọi hàm này,
    /// nên nó phải idempotent (xem ConfirmOrderAsync).
    /// </summary>
    public async Task<PaymentCallbackResult> HandleVnpayIpnAsync(
        VnpayIpnDTO ipn, IReadOnlyDictionary<string, string> rawQuery)
    {
        _logger.LogInformation("VNPay IPN received: txnRef={TxnRef} responseCode={Code}",
            ipn.vnp_TxnRef, ipn.vnp_ResponseCode);

        // Xác thực chữ ký từ query string gốc
        if (!_vnpay.VerifySignature(rawQuery))
        {
            _logger.LogWarning("VNPay IPN signature invalid for order {TxnRef}", ipn.vnp_TxnRef);
            return PaymentCallbackResult.InvalidSignature;
        }

        return await ConfirmOrderAsync(new ConfirmRequest(
            Provider:       PaymentProviders.Vnpay,
            OrderCode:      ipn.vnp_TxnRef,
            // VNPay gửi vnp_Amount đã nhân 100; không chia hết cho 100 → -1 để chắc chắn bị coi là sai số tiền
            AmountVnd:      ipn.vnp_Amount % 100 == 0 ? ipn.vnp_Amount / 100 : -1,
            // VNPay yêu cầu CẢ ResponseCode và TransactionStatus đều là "00"
            GatewaySuccess: ipn.vnp_ResponseCode == "00" && ipn.vnp_TransactionStatus == "00",
            GatewayTransId: ipn.vnp_TransactionNo,
            RawCallback:    JsonSerializer.Serialize(ipn),
            FailureReason:  $"VNPay ResponseCode={ipn.vnp_ResponseCode}, TransactionStatus={ipn.vnp_TransactionStatus}"));
    }

    public async Task<PaymentCallbackResult> HandleSepayIpnAsync(SepayIpnDTO ipn, string? secretHeader)
    {
        // SePay IPN không có chữ ký, chỉ có header X-Secret-Key. Xác thực TRƯỚC khi chạm vào khóa/DB
        // để kẻ lạ không thể khóa mã đơn hay dò đơn.
        if (!_sepay.VerifyIpnSecret(secretHeader))
        {
            _logger.LogWarning("SePay IPN rejected: invalid X-Secret-Key");
            return PaymentCallbackResult.InvalidSignature;
        }

        var invoice = ipn.Order?.InvoiceNumber;
        _logger.LogInformation("SePay IPN received: type={Type} invoice={Invoice}", ipn.NotificationType, invoice);

        if (ipn.NotificationType != "ORDER_PAID")
        {
            // TRANSACTION_VOID: chưa tự thu hồi gói, chỉ log để admin xem xét thủ công
            _logger.LogWarning("SePay IPN {Type} for {Invoice} — cần xem xét thủ công",
                ipn.NotificationType, invoice);
            return PaymentCallbackResult.Ignored;
        }

        if (ipn.Order is null || ipn.Transaction is null || string.IsNullOrWhiteSpace(ipn.Order.InvoiceNumber))
        {
            _logger.LogWarning("SePay IPN ORDER_PAID thiếu order/transaction/invoice");
            return PaymentCallbackResult.OrderNotFound;
        }

        // Trạng thái lạ → bỏ qua, KHÔNG đánh Failed (tránh chặn một lần thanh toán hợp lệ đến sau)
        if (ipn.Order.Status != "CAPTURED" || ipn.Transaction.Status != "APPROVED")
        {
            _logger.LogWarning("SePay IPN ORDER_PAID nhưng order_status={OrderStatus} transaction_status={TxStatus} ({Invoice})",
                ipn.Order.Status, ipn.Transaction.Status, invoice);
            return PaymentCallbackResult.Ignored;
        }

        var amount = ipn.Order.Amount; // VND, không nhân 100; "50000.00" đã được đọc thành decimal
        return await ConfirmOrderAsync(new ConfirmRequest(
            Provider:       PaymentProviders.Sepay,
            OrderCode:      ipn.Order.InvoiceNumber,
            AmountVnd:      amount == decimal.Truncate(amount) ? (long)amount : -1,
            GatewaySuccess: true,
            GatewayTransId: ipn.Transaction.TransactionId,
            RawCallback:    JsonSerializer.Serialize(ipn),
            FailureReason:  string.Empty));
    }

    private sealed record ConfirmRequest(
        string  Provider,
        string  OrderCode,
        long    AmountVnd,        // VND thật (đã quy đổi); âm = không hợp lệ
        bool    GatewaySuccess,
        string? GatewayTransId,
        string  RawCallback,
        string  FailureReason);

    /// <summary>
    /// Lõi xác nhận dùng chung cho mọi cổng: khóa nguyên tử theo mã đơn → tìm đơn (đúng provider)
    /// → idempotent → kiểm tra amount → cập nhật trạng thái → nâng gói → invalidate cache.
    /// </summary>
    private async Task<PaymentCallbackResult> ConfirmOrderAsync(ConfirmRequest r)
    {
        // Trình duyệt và cổng thường tới gần như cùng lúc. INCR nguyên tử làm khóa: chỉ một request
        // được xử lý, tránh cộng ngày hai lần. Khóa tự hết sau 30 giây.
        if (await _cache.IncrementAsync($"payment:ipn:{r.OrderCode}", TimeSpan.FromSeconds(30)) > 1)
        {
            _logger.LogInformation("{Provider}: order {OrderCode} is being processed elsewhere",
                r.Provider, r.OrderCode);
            return PaymentCallbackResult.Busy;
        }

        var orders = await _orderRepo.FindAsync(o => o.OrderCode == r.OrderCode);
        var order  = orders.FirstOrDefault();

        // Đơn của cổng này mới được xác nhận qua endpoint của cổng này
        // (đơn VNPay không thể bị "xác nhận" bằng IPN giả của SePay và ngược lại).
        if (order == null || !string.Equals(order.Provider, r.Provider, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("{Provider}: order not found (or provider mismatch) {OrderCode}",
                r.Provider, r.OrderCode);
            return PaymentCallbackResult.OrderNotFound;
        }

        // Thanh toán MUỘN: đơn đã bị chốt Expired (worker, hoặc bị thay bằng đơn mới) nhưng cổng báo
        // thành công → tiền đã bị trừ nên vẫn phải cấp gói. Chỉ áp dụng cho callback THÀNH CÔNG;
        // callback thất bại cho đơn Expired vẫn bị bỏ qua.
        var isLateSuccess = order.Status == PaymentStatus.Expired && r.GatewaySuccess;
        if (isLateSuccess)
        {
            _logger.LogWarning("{Provider}: late payment for expired order {OrderCode} — confirming",
                r.Provider, order.OrderCode);
            order.FailureReason = null;
        }

        // Idempotent — tránh xử lý 2 lần
        if (order.Status != PaymentStatus.Pending && !isLateSuccess)
        {
            _logger.LogInformation("{Provider}: order {OrderCode} already processed ({Status})",
                r.Provider, order.OrderCode, order.Status);
            return PaymentCallbackResult.AlreadyConfirmed;
        }

        // Kiểm tra số tiền khớp
        if (r.AmountVnd != order.Amount)
        {
            _logger.LogWarning("{Provider} amount mismatch: expected={Expected} got={Got} for order {OrderCode}",
                r.Provider, order.Amount, r.AmountVnd, order.OrderCode);

            order.Status        = PaymentStatus.Failed;
            order.FailureReason = $"Amount mismatch: expected {order.Amount}, got {r.AmountVnd}";
            order.RawCallback   = r.RawCallback;
            _orderRepo.Update(order);
            await _orderRepo.SaveChangesAsync();
            return PaymentCallbackResult.InvalidAmount;
        }

        order.RawCallback    = r.RawCallback;
        order.GatewayTransId = r.GatewayTransId;

        string? upgradedEmail = null;
        if (r.GatewaySuccess)
        {
            order.Status  = PaymentStatus.Success;
            order.PaidAt  = DateTime.UtcNow;
            upgradedEmail = await UpgradeSubscriptionAsync(order); // chỉ gom thay đổi, chưa lưu
        }
        else
        {
            order.Status        = PaymentStatus.Failed;
            order.FailureReason = r.FailureReason;
        }

        // Các repository dùng chung một MovieDbContext (scoped) nên MỘT lần SaveChanges lưu nguyên tử
        // cả đơn hàng, gói và user.
        _orderRepo.Update(order);
        await _orderRepo.SaveChangesAsync();

        if (r.GatewaySuccess)
            await InvalidateUserCacheAsync(order.UserId, upgradedEmail);

        _logger.LogInformation("{Provider} callback processed: order={OrderCode} success={Success}",
            r.Provider, order.OrderCode, r.GatewaySuccess);

        return PaymentCallbackResult.Confirmed;
    }

    // ── Subscription upgrade ──────────────────────────────────────────────────

    /// <summary>
    /// Thanh toán thành công: upsert UserSubscription (cộng thêm ngày nếu đang còn Premium)
    /// và đồng bộ User.SubscriptionType. Chỉ gom thay đổi, việc lưu DB do caller làm.
    /// </summary>
    private async Task<string?> UpgradeSubscriptionAsync(PaymentOrder order)
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
            sub.StartedAt          = ResolveStart(sub, now);
            sub.SubscriptionType   = SubscriptionTypes.Premium;
            sub.ExpiredAt          = newExpiry;
            sub.LastPaymentOrderId = order.Id;
            sub.UpdatedAt          = now;
            _subRepo.Update(sub);
        }

        // User đã được theo dõi nên không gọi Update(user):
        // Update ghi đè mọi cột (kể cả IsActive nếu admin vừa khóa user).
        var user = await _userRepo.GetByIdAsync(order.UserId);
        if (user != null)
        {
            user.SubscriptionType = SubscriptionTypes.Premium;
            user.UpdatedAt        = now;
        }

        _logger.LogInformation(
            "Upgraded user {UserId} to Premium until {Expiry} (order={OrderCode})",
            order.UserId, newExpiry, order.OrderCode);

        return user?.Email;
    }

    /// <summary>Gói còn hạn giữ ngày kích hoạt cũ; gói đã hết hạn thì bắt đầu lại từ bây giờ.</summary>
    private static DateTime ResolveStart(UserSubscription sub, DateTime now) =>
        sub.ExpiredAt is { } e && e > now && sub.StartedAt is { } started ? started : now;

    // ── Query helpers ─────────────────────────────────────────────────────────

    public async Task<IEnumerable<PaymentHistoryDTO>> GetPaymentHistoryAsync(Guid userId)
    {
        var orders = await _orderRepo.FindAsync(o => o.UserId == userId);
        var nowUtc = DateTime.UtcNow;

        return orders
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new PaymentHistoryDTO
            {
                Id            = o.Id,
                OrderCode     = o.OrderCode,
                PlanName      = o.PlanName,
                Amount        = o.Amount,
                Provider      = o.Provider,
                // Đơn Pending quá hạn hiển thị là "expired" (chỉ là cách hiển thị, DB không đổi
                // nên callback đến muộn vẫn được xử lý như cũ).
                Status        = EffectiveStatus(o, nowUtc),
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
    /// Upsert UserSubscription + đồng bộ User.SubscriptionType trong MỘT lần SaveChanges
    /// (các repository dùng chung một DbContext), tránh trạng thái nửa vời nếu bước sau lỗi.
    /// </summary>
    public async Task AdminGrantSubscriptionAsync(AdminGrantSubscriptionDTO dto)
    {
        _ = Plans.FirstOrDefault(p => p.PlanId == dto.PlanId)
            ?? throw new ArgumentException($"Gói '{dto.PlanId}' không tồn tại.");

        var user = await _userRepo.GetByIdAsync(dto.UserId)
                   ?? throw new ArgumentException("Không tìm thấy user.");

        var now        = DateTime.UtcNow;
        var expiredUtc = dto.ExpiredAt.ToUniversalTime();
        if (expiredUtc <= now)
            throw new ArgumentException("Ngày hết hạn phải ở tương lai.");

        var subs = await _subRepo.FindAsync(s => s.UserId == dto.UserId);
        var sub  = subs.FirstOrDefault();

        if (sub == null)
        {
            sub = new UserSubscription
            {
                UserId           = dto.UserId,
                SubscriptionType = SubscriptionTypes.Premium,
                StartedAt        = now,
                ExpiredAt        = expiredUtc,
                UpdatedAt        = now
            };
            await _subRepo.AddAsync(sub);
        }
        else
        {
            sub.StartedAt        = ResolveStart(sub, now);
            sub.SubscriptionType = SubscriptionTypes.Premium;
            sub.ExpiredAt        = expiredUtc;
            sub.UpdatedAt        = now;
            _subRepo.Update(sub);
        }

        // user đã được theo dõi → không cần Update(user)
        user.SubscriptionType = SubscriptionTypes.Premium;
        user.UpdatedAt        = now;

        await _subRepo.SaveChangesAsync();

        await InvalidateUserCacheAsync(dto.UserId, user.Email);

        _logger.LogInformation(
            "[Admin] Granted Premium to user {UserId} until {Expiry} (plan={PlanId})",
            dto.UserId, expiredUtc, dto.PlanId);
    }

    /// <summary>
    /// [Admin] Thu hồi Premium của user: xóa UserSubscription, reset User.SubscriptionType = Free, invalidate cache.
    /// </summary>
    public async Task AdminRevokeSubscriptionAsync(Guid userId)
    {
        var user = await _userRepo.GetByIdAsync(userId)
                   ?? throw new ArgumentException("Không tìm thấy user.");

        var subs = (await _subRepo.FindAsync(s => s.UserId == userId)).ToList();
        foreach (var sub in subs)
            _subRepo.Remove(sub);

        user.SubscriptionType = SubscriptionTypes.Free;
        user.UpdatedAt        = DateTime.UtcNow;

        await _userRepo.SaveChangesAsync();

        await InvalidateUserCacheAsync(userId, user.Email);

        _logger.LogInformation("[Admin] Revoked Premium from user {UserId}", userId);
    }

    /// <summary>
    /// User tự hủy Premium của chính mình (chỉ khi đang có Premium còn hạn).
    /// Tách riêng khỏi AdminRevokeSubscriptionAsync để logic/log/quyền không dùng chung.
    /// </summary>
    public async Task CancelMySubscriptionAsync(Guid userId)
    {
        var user = await _userRepo.GetByIdAsync(userId)
                   ?? throw new ArgumentException("Không tìm thấy user.");

        var subs = (await _subRepo.FindAsync(s => s.UserId == userId)).ToList();
        var now  = DateTime.UtcNow;

        var hasActivePremium = subs.Any(s =>
            s.SubscriptionType == SubscriptionTypes.Premium && s.ExpiredAt > now);
        if (!hasActivePremium)
            throw new ArgumentException("Bạn không có gói Premium đang hoạt động.");

        foreach (var sub in subs)
            _subRepo.Remove(sub);

        user.SubscriptionType = SubscriptionTypes.Free;
        user.UpdatedAt        = now;

        await _userRepo.SaveChangesAsync();

        await InvalidateUserCacheAsync(userId, user.Email);

        _logger.LogInformation("[User] Cancelled own Premium: user {UserId}", userId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// ORD-20241006-9F3A07C1 — 8 ký tự hex ngẫu nhiên mật mã (~4,3 tỷ giá trị/ngày) thay vì 4 ký tự
    /// (65.536/ngày, dễ trùng sau vài trăm đơn).
    /// </summary>
    private static string GenerateOrderCode()
    {
        var date   = DateTime.UtcNow.ToString("yyyyMMdd");
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        return $"ORD-{date}-{suffix}";
    }

    /// <summary>Sinh mã và kiểm tra chưa tồn tại. Vẫn nên có unique index ở DB làm chốt cuối.</summary>
    private async Task<string> GenerateUniqueOrderCodeAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = GenerateOrderCode();
            if (!(await _orderRepo.FindAsync(o => o.OrderCode == code)).Any())
                return code;
        }
        throw new InvalidOperationException("Không thể tạo mã đơn duy nhất. Vui lòng thử lại.");
    }

    /// <summary>
    /// Chốt Expired các đơn Pending còn hạn của user (khi họ tạo đơn mới). Chỉ gom thay đổi,
    /// caller lưu cùng đơn mới. Đơn Pending đã quá hạn để worker xử lý.
    /// </summary>
    private async Task SupersedePendingOrdersAsync(Guid userId, DateTime nowUtc)
    {
        var open = (await _orderRepo.FindAsync(o =>
            o.UserId == userId && o.Status == PaymentStatus.Pending && o.ExpiredAt > nowUtc)).ToList();

        foreach (var o in open)
        {
            o.Status        = PaymentStatus.Expired;
            o.FailureReason = "Thay bằng đơn mới";
            _orderRepo.Update(o);
        }

        if (open.Count > 0)
            _logger.LogInformation("Superseded {Count} open pending order(s) of user {UserId}", open.Count, userId);
    }

    private async Task InvalidateUserCacheAsync(Guid userId, string? email)
    {
        await _cache.RemoveAsync(UserCacheKeys.ById(userId));
        await _cache.RemoveAsync(string.Format(SUB_CACHE_KEY, userId));

        // Key riêng của SubscriptionChecker, tránh user vừa thanh toán xong mà content vẫn bị chặn
        await _cache.RemoveAsync($"subscription:{userId}:isPremium");

        if (!string.IsNullOrEmpty(email))
            await _cache.RemoveAsync(UserCacheKeys.ByEmail(email));
    }

    /// <summary>
    /// Đơn Pending đã quá ExpiredAt coi là Expired khi THỐNG KÊ / LIỆT KÊ cho admin.
    /// Chỉ là cách hiển thị: DB không bị đổi nên IPN đến muộn vẫn xử lý được như cũ.
    /// </summary>
    private static string EffectiveStatus(PaymentOrder o, DateTime nowUtc) =>
        o.Status == PaymentStatus.Pending && o.ExpiredAt <= nowUtc ? PaymentStatus.Expired : o.Status;

    // ── Hết hạn đơn Pending ───────────────────────────────────────────────────

    public async Task<int> ExpireStaleOrdersAsync(CancellationToken ct = default)
    {
        var n = await _paymentOrders.ExpireStalePendingAsync(DateTime.UtcNow - ExpiryGrace, ct);
        if (n > 0)
            _logger.LogInformation("Expired {Count} stale pending orders", n);
        return n;
    }
}
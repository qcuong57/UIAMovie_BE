using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using UIAMovie.API.Services;
using UIAMovie.Controllers;
using UIAMovie.Filter;
using UIAMovie.Hubs;
using UIAMovie.Services;
using UIAMovie.Application.AI.Intent;
using UIAMovie.Application.AI.Orchestration;
using UIAMovie.Application.AI.Retrieval;
using UIAMovie.Application.AI.Tools;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Application.Security;
using UIAMovie.Application.Services;
using UIAMovie.Application.Validators;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.AI.Providers;
using UIAMovie.Infrastructure.AI.Resilience;
using UIAMovie.Infrastructure.Caching;
using UIAMovie.Infrastructure.Configuration;
using UIAMovie.Infrastructure.Data;
using UIAMovie.Infrastructure.Data.Repositories;
using UIAMovie.Infrastructure.Email;
using UIAMovie.Infrastructure.Messaging;
using UIAMovie.Infrastructure.Repositories;
using UIAMovie.Infrastructure.Security;
using UIAMovie.Infrastructure.Services;
using UIAMovie.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Không đặt MaxRequestBodySize toàn cục: các endpoint upload đã có [RequestSizeLimit] riêng.

// Database
builder.Services.AddDbContext<MovieDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Redis
var redisUrl = builder.Configuration["Redis:ConnectionString"];
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var uri = new Uri(redisUrl!);
    var host = uri.Host;
    var port = uri.Port;
    var pass = Uri.UnescapeDataString(uri.UserInfo.Split(':')[1]);

    var options = new ConfigurationOptions
    {
        EndPoints = { { host, port } },
        Password = pass,
        Ssl = true,
        SslProtocols = System.Security.Authentication.SslProtocols.Tls12,
        AbortOnConnectFail = false,
        ConnectRetry = 5,
        ConnectTimeout = 10000,
        SyncTimeout = 10000,
    };

    return ConnectionMultiplexer.Connect(options);
});

builder.Services.AddScoped<ICacheService, RedisCacheService>();

// Repositories
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IMovieRepository, MovieRepository>();
builder.Services.AddScoped<ITvShowRepository, TvShowRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IRepository<Person>, Repository<Person>>();
builder.Services.AddScoped<IRepository<MovieCast>, Repository<MovieCast>>();
builder.Services.AddScoped<IRepository<MovieDirector>, Repository<MovieDirector>>();
builder.Services.AddScoped<IRepository<MovieImage>, Repository<MovieImage>>();
builder.Services.AddScoped<IViewSessionRepository, ViewSessionRepository>(); // Infrastructure.Data.Repositories
builder.Services.AddScoped<IViewStatsRepository, ViewStatsRepository>();


// Authentication
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<ITwoFactorAuthProvider, TwoFactorAuthProvider>();
builder.Services.AddScoped<IEmailService, EmailService>();

// Email queue chạy nền (gửi OTP không làm chậm request)
builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddSingleton<IEmailQueue>(sp => sp.GetRequiredService<EmailQueue>());
builder.Services.AddHostedService<EmailQueueWorker>();

// Dọn UserSession hết hạn định kỳ
builder.Services.AddHostedService<SessionCleanupWorker>();

// ── SignalR & Realtime Notification (Đăng ký tại đây) ────────────────────────
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotificationSender, RealtimeNotificationSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IMovieService, MovieService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITokenRevocationService, TokenRevocationService>();
builder.Services.AddValidatorsFromAssemblyContaining(typeof(RegisterValidator));
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ITmdbService, TmdbService>();
builder.Services.AddScoped<IRatingReviewService, RatingReviewService>();
builder.Services.AddScoped<IGenreService, GenreService>();


// AI
builder.Services.Configure<GroqOptions>(builder.Configuration.GetSection("Groq"));
builder.Services.AddSingleton<IAiRateLimiter, SlidingWindowAiRateLimiter>();
builder.Services.AddHttpClient<IAiProvider, GroqProvider>();
builder.Services.AddScoped<IMovieRetriever, MovieRetriever>();
builder.Services.AddScoped<ITvShowRetriever, TvShowRetriever>();
builder.Services.AddScoped<ISiteKnowledgeRetriever, SiteKnowledgeRetriever>();
builder.Services.AddScoped<IUserContextRetriever, UserContextRetriever>();
builder.Services.AddScoped<IAiRouter, AiRouter>();
builder.Services.AddScoped<MovieCompareTool>();
builder.Services.AddScoped<ReviewSummaryTool>();
builder.Services.AddScoped<IAiAssistantService, AiAssistantService>();
builder.Services.AddScoped<IGroqService, GroqService>(); // không dùng typed HttpClient: GroqService gọi AI qua IAiProvider
builder.Services.AddScoped<ITvShowService, TvShowService>();
builder.Services.AddScoped<IRepository<TvShowVideo>, Repository<TvShowVideo>>();
builder.Services.AddScoped<IRepository<TvShowImage>, Repository<TvShowImage>>();
builder.Services.AddScoped<IRepository<TvShowGenre>, Repository<TvShowGenre>>();
builder.Services.AddScoped<IRepository<TvShowCast>, Repository<TvShowCast>>();
builder.Services.AddScoped<IPersonRepository, PersonRepository>();
builder.Services.AddScoped<IPersonService, PersonService>();
builder.Services.AddScoped<IRepository<TvShowDirector>, Repository<TvShowDirector>>();
builder.Services.AddScoped<ISubtitleRepository, SubtitleRepository>();
builder.Services.AddScoped<IEpisodeSubtitleRepository, EpisodeSubtitleRepository>();
builder.Services.AddScoped<ISubtitleService, SubtitleService>();
builder.Services.AddScoped<IEpisodeSubtitleService, EpisodeSubtitleService>();
builder.Services.AddScoped<IRepository<Season>, Repository<Season>>();
builder.Services.AddScoped<IRepository<Episode>, Repository<Episode>>();
builder.Services.AddScoped<ISubscriptionChecker, SubscriptionChecker>();
builder.Services.AddScoped<IAdRepository, AdRepository>();
builder.Services.AddScoped<IAdService, AdService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IVnpayPaymentService, VnpayPaymentService>();
builder.Services.AddScoped<IPlaybackService, PlaybackService>();
builder.Services.AddScoped<IViewTrackingService, ViewTrackingService>();
builder.Services.AddScoped<IViewStatsService, ViewStatsService>();
builder.Services.AddScoped<IPaymentOrderRepository, PaymentOrderRepository>();
builder.Services.AddScoped<IRevenueService, RevenueService>();
builder.Services.AddHostedService<PaymentExpiryWorker>();
builder.Services.Configure<SepayOptions>(builder.Configuration.GetSection("Sepay"));
builder.Services.AddScoped<ISepayPaymentService, SepayPaymentService>();
builder.Services.AddSingleton<IClock, VnClock>(); // "hôm nay" theo giờ VN cho ViewDate/thống kê/trending
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IContentMetaProvider, ContentMetaProvider>(); // cache metadata nhẹ cho heartbeat
builder.Services.Configure<VnpayOptions>(builder.Configuration.GetSection("VNPay"));

// JWT Configuration & SignalR Token Extraction
var jwtSettings = builder.Configuration.GetSection("Jwt");
var jwtKeyBytes = System.Text.Encoding.UTF8.GetBytes(
    jwtSettings["SecretKey"] ?? throw new InvalidOperationException("Thiếu cấu hình Jwt:SecretKey"));
if (jwtKeyBytes.Length < 32)
    throw new InvalidOperationException("Jwt:SecretKey phải dài ít nhất 32 byte (HS256)");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(jwtKeyBytes),
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },

            // Token còn hạn nhưng user đã bị khóa/xóa/đổi role/đổi mật khẩu: từ chối nếu token phát hành trước mốc thu hồi.
            // Redis lỗi thì coi như chưa bị thu hồi (fail-open, không làm sập toàn bộ API).
            OnTokenValidated = async context =>
            {
                var idClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(idClaim, out var uid)) return;

                long.TryParse(context.Principal!.FindFirst("iat")?.Value, out var iat);

                var revocation = context.HttpContext.RequestServices.GetRequiredService<ITokenRevocationService>();
                if (await revocation.IsRevokedAsync(uid, iat))
                    context.Fail("Token đã bị thu hồi");
            }
        };
    });

// CORS: Whitelist domain
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? new[]
                     {
                         "http://localhost:3000",
                         "http://localhost:5173",
                         "https://uiamovie.vercel.app"
                     };

// Cùng danh sách này được AuthController dùng để kiểm tra Origin của /auth/refresh-token
builder.Services.AddSingleton(new AllowedOriginList(allowedOrigins));

builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders("X-Cache-Invalidated"));
});

// Built-in Rate Limiter
// Lưu ý: limiter lưu trong bộ nhớ từng instance. Chạy N instance thì limit thực tế nhân N.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            "{\"message\":\"Bạn thao tác quá nhanh, vui lòng thử lại sau.\",\"statusCode\":429}", ct);
    };

    static string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    static string? UserId(HttpContext c) =>
        c.User.Identity?.IsAuthenticated == true
            ? (c.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? c.User.FindFirst("sub")?.Value)
            : null;
    static FixedWindowRateLimiterOptions Fixed(int permit) => new()
        { PermitLimit = permit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };

    options.AddPolicy("auth-strict", c =>
        RateLimitPartition.GetFixedWindowLimiter(Ip(c), _ => Fixed(10)));

    // Mỗi lần mở tab/reload đều gọi refresh (giai đoạn 4) nên thoáng hơn auth-strict
    options.AddPolicy("auth-refresh", c =>
        RateLimitPartition.GetFixedWindowLimiter(Ip(c), _ => Fixed(60)));

    options.AddPolicy("ai", c => UserId(c) is { } uid
        ? RateLimitPartition.GetFixedWindowLimiter("u:" + uid, _ => Fixed(20))
        : RateLimitPartition.GetFixedWindowLimiter("ip:" + Ip(c), _ => Fixed(5)));

    options.AddPolicy("playback", c => UserId(c) is { } uid
        ? RateLimitPartition.GetFixedWindowLimiter("u:" + uid, _ => Fixed(30))
        : RateLimitPartition.GetFixedWindowLimiter("ip:" + Ip(c), _ => Fixed(30)));

    // User đăng nhập: theo userId. Guest: theo IP (guest id do client gửi nên giả được, không dùng làm khóa).
    // ~4 beat/phút/tab: 20/phút/user là dư; 240/phút/IP ≈ 50 tab cùng IP (NAT, ký túc xá).
    options.AddPolicy("heartbeat", c => UserId(c) is { } uid
        ? RateLimitPartition.GetFixedWindowLimiter("u:" + uid, _ => Fixed(20))
        : RateLimitPartition.GetFixedWindowLimiter("ip:" + Ip(c), _ => Fixed(240)));
});

// Chạy sau reverse proxy (Render): chỉ tin X-Forwarded-* từ proxy trong mạng riêng,
// nên client ngoài internet không giả được X-Forwarded-For.
// .NET 10: KnownNetworks đổi tên thành KnownIPNetworks và dùng System.Net.IPNetwork.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("fc00::"), 7));
    options.ForwardLimit = null; // đọc XFF từ phải sang trái, dừng ở IP đầu tiên không tin cậy
});

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ValidationErrorFilter.Handler;
    })
    .AddJsonOptions(options => { options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders(); // phải đứng đầu pipeline, trước UseHttpsRedirection
app.UseHttpsRedirection();

// Security Headers
app.Use(async (context, next) =>
{
    // Dùng indexer (ghi đè) thay vì Append để header không bị lặp nếu nơi khác đã đặt.
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    // Đã bỏ X-XSS-Protection (lỗi thời; trình duyệt hiện đại bỏ qua, bản cũ còn có thể gây lỗi).
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        // CSP cho API (chỉ trả JSON): không cho tải/chạy gì cả. Bỏ qua ở Development vì Swagger UI cần script.
        // CSP của giao diện (React) nằm ở vercel.json, vì HTML do Vercel phục vụ chứ không phải backend này.
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    }

    await next();
});

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("Default");
app.UseAuthentication();
app.UseRateLimiter(); // sau Authentication để policy đọc được userId
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications"); // Map SignalR Route
app.MapGet("/", () => "UIAMovie API running");

app.Run();
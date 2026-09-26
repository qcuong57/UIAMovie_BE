using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using UIAMovie.API.Filters;
using UIAMovie.API.Hubs;
using UIAMovie.API.Services;
using UIAMovie.Application.AI.Intent;
using UIAMovie.Application.AI.Orchestration;
using UIAMovie.Application.AI.Retrieval;
using UIAMovie.Application.AI.Tools;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Services;
using UIAMovie.Application.Services.Payment;
using UIAMovie.Application.Validators;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.AI.Providers;
using UIAMovie.Infrastructure.AI.Resilience;
using UIAMovie.Infrastructure.Caching;
using UIAMovie.Infrastructure.Configuration;
using UIAMovie.Infrastructure.Data;
using UIAMovie.Infrastructure.Data.Repositories;
using UIAMovie.Infrastructure.Messaging;
using UIAMovie.Infrastructure.Security;
using UIAMovie.Infrastructure.Services;
using UIAMovie.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500 * 1024 * 1024;
});

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
builder.Services.AddScoped<IMovieRepository, MovieRepository>();
builder.Services.AddScoped<ITvShowRepository, TvShowRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IRepository<Person>, Repository<Person>>();
builder.Services.AddScoped<IRepository<MovieCast>, Repository<MovieCast>>();
builder.Services.AddScoped<IRepository<MovieDirector>, Repository<MovieDirector>>();
builder.Services.AddScoped<IRepository<MovieImage>, Repository<MovieImage>>();

// Authentication
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<ITwoFactorAuthProvider, TwoFactorAuthProvider>();
builder.Services.AddScoped<IEmailService, EmailService>();

// ── SignalR & Realtime Notification (Đăng ký tại đây) ────────────────────────
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotificationSender, RealtimeNotificationSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IMovieService, MovieService>();
builder.Services.AddScoped<IUserService, UserService>();
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
builder.Services.AddHttpClient<IGroqService, GroqService>();
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
builder.Services.Configure<VnpayOptions>(builder.Configuration.GetSection("VNPay"));

// JWT Configuration & SignalR Token Extraction
var jwtSettings = builder.Configuration.GetSection("Jwt");
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
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!)),
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
            }
        };
    });

// CORS: Whitelist domain
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000", "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

// Built-in Rate Limiter
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-strict", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ValidationErrorFilter.Handler;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Security Headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }

    await next();
});

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("Default");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications"); // Map SignalR Route
app.MapGet("/", () => "UIAMovie API running");

app.Run();
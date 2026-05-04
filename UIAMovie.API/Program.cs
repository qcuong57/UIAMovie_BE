using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using StackExchange.Redis;
using UIAMovie.API.Filters;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Services;
using UIAMovie.Application.Validators;
using UIAMovie.Domain.Entities;
using UIAMovie.Infrastructure.Caching;
using UIAMovie.Infrastructure.Configuration;
using UIAMovie.Infrastructure.Data;
using UIAMovie.Infrastructure.Data.Repositories;
using UIAMovie.Infrastructure.Messaging;
using UIAMovie.Infrastructure.Security;
using UIAMovie.Infrastructure.Services;
using UIAMovie.Middleware;


var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<MovieDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Redis
var redisUrl = builder.Configuration["Redis:ConnectionString"];
 
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var uri    = new Uri(redisUrl!);
    var host   = uri.Host;
    var port   = uri.Port;
    var pass   = Uri.UnescapeDataString(uri.UserInfo.Split(':')[1]);
 
    var options = new ConfigurationOptions
    {
        EndPoints          = { { host, port } },
        Password           = pass,
        Ssl                = true,
        SslProtocols       = System.Security.Authentication.SslProtocols.Tls12,
        AbortOnConnectFail = false,
        ConnectRetry       = 5,
        ConnectTimeout     = 10000,
        SyncTimeout        = 10000,
    };
 
    return ConnectionMultiplexer.Connect(options);
});
 
builder.Services.AddScoped<ICacheService, RedisCacheService>();

// Repositories
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IMovieRepository, MovieRepository>();
builder.Services.AddScoped<ITvShowRepository, TvShowRepository>();

// Authentication
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<ITwoFactorAuthProvider, TwoFactorAuthProvider>();
builder.Services.AddScoped<IEmailService, EmailService>();

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
builder.Services.AddScoped<IRepository<Person>, Repository<Person>>();
builder.Services.AddScoped<IRepository<MovieCast>, Repository<MovieCast>>();
builder.Services.AddScoped<IRepository<MovieDirector>, Repository<MovieDirector>>();
builder.Services.AddScoped<IRepository<MovieImage>, Repository<MovieImage>>();
builder.Services.AddHttpClient<IGroqService, GroqService>();
builder.Services.AddScoped<ITvShowService, TvShowService>();
builder.Services.AddScoped<IRepository<TvShowVideo>,   Repository<TvShowVideo>>();
builder.Services.AddScoped<IRepository<TvShowImage>,   Repository<TvShowImage>>();
builder.Services.AddScoped<IRepository<TvShowGenre>,   Repository<TvShowGenre>>();
builder.Services.AddScoped<IRepository<TvShowCast>,    Repository<TvShowCast>>();
builder.Services.AddScoped<IRepository<TvShowDirector>, Repository<TvShowDirector>>();
builder.Services.AddScoped<IRepository<Season>,        Repository<Season>>();
builder.Services.AddScoped<IRepository<Episode>,       Repository<Episode>>();

// JWT Configuration
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
                System.Text.Encoding.ASCII.GetBytes(jwtSettings["SecretKey"]))
        };
    });

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ValidationErrorFilter.Handler;
    });builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger luôn bật (cả Production)
app.UseSwagger();
app.UseSwaggerUI();

// Middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<JwtMiddleware>();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", () => "UIAMovie API running");

app.Run();
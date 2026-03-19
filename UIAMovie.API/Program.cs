using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using StackExchange.Redis;
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
var redisConnection = builder.Configuration["Redis:ConnectionString"];
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddScoped<ICacheService, RedisCacheService>();

// Repositories
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IMovieRepository, MovieRepository>();
// builder.Services.AddScoped<IRepository<RatingReview>, Repository<RatingReview>>();

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

// Thêm Kafka
// builder.Services.AddScoped<IKafkaProducer, KafkaProducer>();
// builder.Services.AddHostedService<VideoUploadConsumerService>();

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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Thêm Middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<JwtMiddleware>();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
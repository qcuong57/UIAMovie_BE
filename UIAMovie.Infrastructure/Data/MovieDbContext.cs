using UIAMovie.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace UIAMovie.Infrastructure.Data;

public class MovieDbContext : DbContext
{
    public MovieDbContext(DbContextOptions<MovieDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<UserSession> UserSessions { get; set; }
    public DbSet<Movie> Movies { get; set; }
    public DbSet<MovieVideo> MovieVideos { get; set; }
    public DbSet<Genre> Genres { get; set; }
    public DbSet<RatingReview> RatingReviews { get; set; }
    public DbSet<MovieGenre> MovieGenres { get; set; }
    public DbSet<Favorite> Favorites { get; set; }
    public DbSet<WatchHistory> WatchHistories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure User
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.SubscriptionType).HasDefaultValue("free");
            entity.Property(e => e.Role).HasDefaultValue("User");
        });

        // Configure Movie
        modelBuilder.Entity<Movie>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TmdbId).IsUnique();
        });

        modelBuilder.Entity<RatingReview>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(r => r.User)
                .WithMany(u => u.RatingReviews)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.Movie)
                .WithMany(m => m.RatingReviews)
                .HasForeignKey(r => r.MovieId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.MovieId }).IsUnique();
        });

        // Configure Favorite
        modelBuilder.Entity<Favorite>(entity => { entity.HasIndex(e => new { e.UserId, e.MovieId }).IsUnique(); });

        // Configure MovieGenre
        modelBuilder.Entity<MovieGenre>(entity => { entity.HasIndex(e => new { e.MovieId, e.GenreId }).IsUnique(); });

        // ─── Seed Admin Account ───────────────────────────────────────────────
        // Password: Admin@12345 (đã hash sẵn bằng BCrypt)
        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000001");


        modelBuilder.Entity<User>().HasData(new User
        {
            Id = adminId,
            Email = "quoccuong572003@gmail.com",
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Quoccuong572003@"),
            Role = "Admin",
            SubscriptionType = "premium",
            IsActive = true,
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
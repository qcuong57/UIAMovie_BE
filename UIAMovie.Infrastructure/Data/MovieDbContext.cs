using UIAMovie.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace UIAMovie.Infrastructure.Data;

public class MovieDbContext : DbContext
{
    public MovieDbContext(DbContextOptions<MovieDbContext> options)
        : base(options)
    {
    }

    // ── DbSets ───────────────────────────────────────────────────────────────
    public DbSet<User>          Users          { get; set; }
    public DbSet<UserSession>   UserSessions   { get; set; }

    public DbSet<Movie>         Movies         { get; set; }
    public DbSet<MovieVideo>    MovieVideos    { get; set; }
    public DbSet<MovieImage>    MovieImages    { get; set; }    // ← mới

    public DbSet<Genre>         Genres         { get; set; }
    public DbSet<MovieGenre>    MovieGenres    { get; set; }

    public DbSet<Person>        People         { get; set; }    // ← mới
    public DbSet<MovieCast>     MovieCasts     { get; set; }    // ← mới
    public DbSet<MovieDirector> MovieDirectors { get; set; }    // ← mới

    public DbSet<Favorite>      Favorites      { get; set; }
    public DbSet<WatchHistory>  WatchHistories { get; set; }
    public DbSet<RatingReview>  RatingReviews  { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── User ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.SubscriptionType).HasDefaultValue("free");
            entity.Property(e => e.Role).HasDefaultValue("User");
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.User)
                  .WithMany(u => u.UserSessions)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Movie ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Movie>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Filter null để không bị unique conflict khi tạo phim thủ công
            entity.HasIndex(e => e.TmdbId).IsUnique().HasFilter("\"TmdbId\" IS NOT NULL");
            entity.Property(e => e.ImdbRating).HasPrecision(4, 1);
        });

        modelBuilder.Entity<MovieVideo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Movie)
                  .WithMany(m => m.MovieVideos)
                  .HasForeignKey(e => e.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MovieImage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Movie)
                  .WithMany(m => m.MovieImages)
                  .HasForeignKey(e => e.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Genre ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Genre>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TmdbGenreId).IsUnique().HasFilter("\"TmdbGenreId\" IS NOT NULL");
        });

        modelBuilder.Entity<MovieGenre>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MovieId, e.GenreId }).IsUnique();

            entity.HasOne(e => e.Movie)
                  .WithMany(m => m.MovieGenres)
                  .HasForeignKey(e => e.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Genre)
                  .WithMany(g => g.MovieGenres)
                  .HasForeignKey(e => e.GenreId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Person / Cast / Director ──────────────────────────────────────────
        modelBuilder.Entity<Person>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TmdbPersonId).IsUnique().HasFilter("\"TmdbPersonId\" IS NOT NULL");
        });

        modelBuilder.Entity<MovieCast>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Cùng 1 diễn viên không xuất hiện 2 lần trong 1 phim
            entity.HasIndex(e => new { e.MovieId, e.PersonId }).IsUnique();

            entity.HasOne(e => e.Movie)
                  .WithMany(m => m.MovieCasts)
                  .HasForeignKey(e => e.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Person)
                  .WithMany(p => p.MovieCasts)
                  .HasForeignKey(e => e.PersonId)
                  .OnDelete(DeleteBehavior.Restrict); // xóa phim không kéo theo xóa người
        });

        modelBuilder.Entity<MovieDirector>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MovieId, e.PersonId }).IsUnique();

            entity.HasOne(e => e.Movie)
                  .WithMany(m => m.MovieDirectors)
                  .HasForeignKey(e => e.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Person)
                  .WithMany(p => p.MovieDirectors)
                  .HasForeignKey(e => e.PersonId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Favorite ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Favorite>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.MovieId }).IsUnique();

            entity.HasOne(e => e.User)
                  .WithMany(u => u.Favorites)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Movie)
                  .WithMany(m => m.Favorites)
                  .HasForeignKey(e => e.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── WatchHistory ──────────────────────────────────────────────────────
        modelBuilder.Entity<WatchHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            // 1 user chỉ có 1 record cho 1 phim — update thay vì insert mới
            entity.HasIndex(e => new { e.UserId, e.MovieId }).IsUnique();

            entity.HasOne(e => e.User)
                  .WithMany(u => u.WatchHistory)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Movie)
                  .WithMany(m => m.WatchHistories)
                  .HasForeignKey(e => e.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── RatingReview ──────────────────────────────────────────────────────
        modelBuilder.Entity<RatingReview>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Không unique — cho phép 1 user review nhiều lần trên cùng 1 phim
            entity.HasIndex(e => new { e.UserId, e.MovieId });

            entity.HasOne(r => r.User)
                  .WithMany(u => u.RatingReviews)
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.Movie)
                  .WithMany(m => m.RatingReviews)
                  .HasForeignKey(r => r.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Seed Admin ────────────────────────────────────────────────────────
        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        modelBuilder.Entity<User>().HasData(new User
        {
            Id               = adminId,
            Email            = "quoccuong572003@gmail.com",
            Username         = "admin",
            PasswordHash     = BCrypt.Net.BCrypt.HashPassword("Quoccuong572003@"),
            Role             = "Admin",
            SubscriptionType = "premium",
            IsActive         = true,
            CreatedAt        = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt        = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}   
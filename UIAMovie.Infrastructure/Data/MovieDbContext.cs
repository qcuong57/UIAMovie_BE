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
    public DbSet<MovieImage>    MovieImages    { get; set; }

    public DbSet<Genre>         Genres         { get; set; }
    public DbSet<MovieGenre>    MovieGenres    { get; set; }

    public DbSet<Person>        People         { get; set; }
    public DbSet<PersonImage>   PersonImages   { get; set; }   // ← mới
    public DbSet<MovieCast>     MovieCasts     { get; set; }
    public DbSet<MovieDirector> MovieDirectors { get; set; }

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

        // ── PersonImage ───────────────────────────────────────────────────────
        modelBuilder.Entity<PersonImage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Person)
                  .WithMany(p => p.Images)
                  .HasForeignKey(e => e.PersonId)
                  .OnDelete(DeleteBehavior.Cascade); // xóa người → xóa ảnh theo
        });

        modelBuilder.Entity<MovieCast>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MovieId, e.PersonId }).IsUnique();

            entity.HasOne(e => e.Movie)
                  .WithMany(m => m.MovieCasts)
                  .HasForeignKey(e => e.MovieId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Person)
                  .WithMany(p => p.MovieCasts)
                  .HasForeignKey(e => e.PersonId)
                  .OnDelete(DeleteBehavior.Restrict);
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
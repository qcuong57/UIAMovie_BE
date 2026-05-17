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
    public DbSet<User> Users { get; set; }
    public DbSet<UserSession> UserSessions { get; set; }

    public DbSet<Movie> Movies { get; set; }
    public DbSet<MovieVideo> MovieVideos { get; set; }
    public DbSet<MovieImage> MovieImages { get; set; }

    public DbSet<Genre> Genres { get; set; }
    public DbSet<MovieGenre> MovieGenres { get; set; }

    public DbSet<Person> People { get; set; }
    public DbSet<PersonImage> PersonImages { get; set; } // ← mới
    public DbSet<MovieCast> MovieCasts { get; set; }
    public DbSet<MovieDirector> MovieDirectors { get; set; }

    public DbSet<Favorite> Favorites { get; set; }
    public DbSet<WatchHistory> WatchHistories { get; set; }
    public DbSet<RatingReview> RatingReviews { get; set; }

    public DbSet<TvShow> TvShows { get; set; }
    public DbSet<Season> Seasons { get; set; }
    public DbSet<Episode> Episodes { get; set; }
    public DbSet<TvShowGenre> TvShowGenres { get; set; }
    public DbSet<TvShowCast> TvShowCasts { get; set; }
    public DbSet<TvShowDirector> TvShowDirectors { get; set; }
    public DbSet<TvShowImage> TvShowImages { get; set; }
    public DbSet<TvShowVideo> TvShowVideos { get; set; }
    public DbSet<TvShowFavorite> TvShowFavorites { get; set; }

    public DbSet<UserSubscription> UserSubscriptions { get; set; }
    public DbSet<PaymentOrder> PaymentOrders { get; set; }

    public DbSet<MovieSubtitle> MovieSubtitles { get; set; }
    public DbSet<EpisodeSubtitle> EpisodeSubtitles { get; set; }


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

        modelBuilder.Entity<MovieSubtitle>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MovieId, e.LanguageCode }).IsUnique();

            entity.HasOne(e => e.Movie)
                .WithMany()
                .HasForeignKey(e => e.MovieId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.LanguageCode).HasMaxLength(10).IsRequired();
            entity.Property(e => e.LanguageName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(50);
            entity.Property(e => e.Status).HasConversion<int>();
        });

        // ── TvShow ────────────────────────────────────────────────────────────────
        modelBuilder.Entity<TvShow>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TmdbId).IsUnique().HasFilter("\"TmdbId\" IS NOT NULL");
            entity.Property(e => e.ImdbRating).HasPrecision(4, 1);
        });

// ── Season ────────────────────────────────────────────────────────────────
        modelBuilder.Entity<Season>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Mỗi TvShow chỉ có 1 season với cùng SeasonNumber
            entity.HasIndex(e => new { e.TvShowId, e.SeasonNumber }).IsUnique();

            entity.HasOne(e => e.TvShow)
                .WithMany(t => t.Seasons)
                .HasForeignKey(e => e.TvShowId)
                .OnDelete(DeleteBehavior.Cascade);
        });

// ── Episode ───────────────────────────────────────────────────────────────
        modelBuilder.Entity<Episode>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Mỗi Season chỉ có 1 episode với cùng EpisodeNumber
            entity.HasIndex(e => new { e.SeasonId, e.EpisodeNumber }).IsUnique();
            entity.Property(e => e.Rating).HasPrecision(4, 1);

            entity.HasOne(e => e.Season)
                .WithMany(s => s.Episodes)
                .HasForeignKey(e => e.SeasonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

// ── TvShowGenre ───────────────────────────────────────────────────────────
        modelBuilder.Entity<TvShowGenre>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TvShowId, e.GenreId }).IsUnique();

            entity.HasOne(e => e.TvShow)
                .WithMany(t => t.TvShowGenres)
                .HasForeignKey(e => e.TvShowId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Genre)
                .WithMany() // Genre không cần nav TvShowGenres
                .HasForeignKey(e => e.GenreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

// ── TvShowCast ────────────────────────────────────────────────────────────
        modelBuilder.Entity<TvShowCast>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TvShowId, e.PersonId }).IsUnique();

            entity.HasOne(e => e.TvShow)
                .WithMany(t => t.TvShowCasts)
                .HasForeignKey(e => e.TvShowId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Person)
                .WithMany() // Person không cần nav TvShowCasts
                .HasForeignKey(e => e.PersonId)
                .OnDelete(DeleteBehavior.Restrict); // Xóa TvShow không xóa Person
        });

// ── TvShowDirector ────────────────────────────────────────────────────────
        modelBuilder.Entity<TvShowDirector>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TvShowId, e.PersonId }).IsUnique();

            entity.HasOne(e => e.TvShow)
                .WithMany(t => t.TvShowDirectors)
                .HasForeignKey(e => e.TvShowId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Person)
                .WithMany()
                .HasForeignKey(e => e.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
        });

// ── TvShowImage ───────────────────────────────────────────────────────────
        modelBuilder.Entity<TvShowImage>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.TvShow)
                .WithMany(t => t.TvShowImages)
                .HasForeignKey(e => e.TvShowId)
                .OnDelete(DeleteBehavior.Cascade);
        });

// ── TvShowVideo ───────────────────────────────────────────────────────────
        modelBuilder.Entity<TvShowVideo>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.TvShow)
                .WithMany(t => t.TvShowVideos)
                .HasForeignKey(e => e.TvShowId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
// ── EpisodeSubtitle ───────────────────────────────────────────────────────────
        modelBuilder.Entity<EpisodeSubtitle>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Một tập phim chỉ có 1 subtitle cho mỗi ngôn ngữ
            entity.HasIndex(e => new { e.EpisodeId, e.LanguageCode }).IsUnique();

            entity.HasOne(e => e.Episode)
                .WithMany() // Episode không cần nav EpisodeSubtitles
                .HasForeignKey(e => e.EpisodeId)
                .OnDelete(DeleteBehavior.Cascade); // Xóa episode → xóa subtitle theo

            entity.Property(e => e.LanguageCode).HasMaxLength(10).IsRequired();
            entity.Property(e => e.LanguageName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(50);
            entity.Property(e => e.Status).HasConversion<int>();
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

        // ── UserSubscription ──────────────────────────────────────────────────
        modelBuilder.Entity<UserSubscription>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId); // query nhanh theo user

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.SubscriptionType)
                .HasMaxLength(50)
                .IsRequired();
        });

        // ── PaymentOrder ──────────────────────────────────────────────────────
        modelBuilder.Entity<PaymentOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OrderCode).IsUnique();
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Amount)
                .HasPrecision(18, 2);
        });

        // ── Seed Admin ────────────────────────────────────────────────────────
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
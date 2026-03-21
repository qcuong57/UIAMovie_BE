namespace UIAMovie.Domain.Entities;

public class Movie
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? TmdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public int? Duration { get; set; }
    public decimal? ImdbRating { get; set; }
    public string? ContentRating  { get; set; }
    /// <summary>Mã quốc gia sản xuất — ISO 3166-1 alpha-2, VD: "US", "KR", "JP"</summary>
    public string? OriginCountry  { get; set; }
    public Guid? UploadedBy { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<MovieGenre> MovieGenres { get; set; } = new List<MovieGenre>();
    public ICollection<MovieVideo> MovieVideos { get; set; } = new List<MovieVideo>();
    public ICollection<MovieCast> MovieCasts { get; set; } = new List<MovieCast>(); // ← mới
    public ICollection<MovieDirector> MovieDirectors { get; set; } = new List<MovieDirector>(); // ← mới
    public ICollection<MovieImage> MovieImages { get; set; } = new List<MovieImage>(); // ← mới
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
    public ICollection<WatchHistory> WatchHistories { get; set; } = new List<WatchHistory>();
    public ICollection<RatingReview> RatingReviews { get; set; } = new List<RatingReview>();
}

// ─────────────────────────────────────────────────────────────────────────────

public class MovieVideo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MovieId { get; set; }
    public string VideoUrl { get; set; } = string.Empty;
    public string VideoType { get; set; } = string.Empty; // trailer, full_movie
    public int? Duration { get; set; }
    public string? Quality { get; set; } // 480p, 720p, 1080p, 4k
    public long? FileSize { get; set; }
    public Guid? UploadedBy { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // FK
    public Movie? Movie { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────

public class MovieImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MovieId { get; set; }
    public string Url { get; set; } = string.Empty;

    /// <summary>"backdrop" | "poster"</summary>
    public string ImageType { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // FK
    public Movie? Movie { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────

public class Favorite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid MovieId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    // FK
    public User? User { get; set; }
    public Movie? Movie { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────

public class WatchHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid MovieId { get; set; }
    public DateTime WatchedAt { get; set; } = DateTime.UtcNow;
    public int ProgressMinutes { get; set; }
    public bool IsCompleted { get; set; }

    // FK
    public User? User { get; set; }
    public Movie? Movie { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────

public class Genre
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? TmdbGenreId { get; set; } // ← để sync với TMDB genre id
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Navigation properties
    public ICollection<MovieGenre> MovieGenres { get; set; } = new List<MovieGenre>();
}

// ─────────────────────────────────────────────────────────────────────────────

public class MovieGenre
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MovieId { get; set; }
    public Guid GenreId { get; set; }

    // FK
    public Movie? Movie { get; set; }
    public Genre? Genre { get; set; }
}
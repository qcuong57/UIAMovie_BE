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
    /// <summary>TRUE = chỉ dành cho user Premium. Mặc định false (free).</summary>
    public bool IsPremium { get; set; } = false;
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

    // ── Streaming (HLS / Adaptive Bitrate) ───────────────────────────────────
    // Tất cả đều nullable để tương thích ngược với các video cũ (chỉ có VideoUrl .mp4).

    /// <summary>"HLS" | "MP4". NULL = video cũ, coi như MP4.</summary>
    public string? StreamingType { get; set; }

    /// <summary>URL phát thực tế (vd: master.m3u8). NULL = dùng VideoUrl.</summary>
    public string? PlaybackUrl { get; set; }

    /// <summary>"cloudinary" | "youtube" | ... NULL = coi như cloudinary.</summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Cloudinary public ID (gồm folder, không có đuôi file). Lưu lúc upload để dựng
    /// link HLS mà không phải parse URL. NULL với video cũ → fallback ExtractPublicId(VideoUrl).
    /// </summary>
    public string? PublicId { get; set; }

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

    /// <summary>Vị trí xem chính xác (giây) để player seek/resume. ProgressMinutes vẫn được giữ đồng bộ (= ProgressSeconds / 60).</summary>
    public int ProgressSeconds { get; set; } = 0;

    /// <summary>Tổng thời lượng video (giây) tại thời điểm xem, dùng tính % tiến độ. NULL với bản ghi cũ.</summary>
    public int? Duration { get; set; }

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
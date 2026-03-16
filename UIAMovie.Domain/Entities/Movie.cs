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
    public string? ContentRating { get; set; }
    public Guid? UploadedBy { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<MovieGenre> MovieGenres { get; set; } = new List<MovieGenre>();
    public ICollection<MovieVideo> MovieVideos { get; set; } = new List<MovieVideo>();
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();
    public ICollection<WatchHistory> WatchHistories { get; set; } = new List<WatchHistory>();
    public ICollection<RatingReview> RatingReviews { get; set; } = new List<RatingReview>();

}

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

    // Foreign Key
    public Movie? Movie { get; set; }
}

public class Favorite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid MovieId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    // Foreign Keys
    public User? User { get; set; }
    public Movie? Movie { get; set; }
}

public class WatchHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid MovieId { get; set; }
    public DateTime WatchedAt { get; set; } = DateTime.UtcNow;
    public int ProgressMinutes { get; set; }
    public bool IsCompleted { get; set; }

    // Foreign Keys
    public User? User { get; set; }
    public Movie? Movie { get; set; }
}

public class Genre
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Navigation properties
    public ICollection<MovieGenre> MovieGenres { get; set; } = new List<MovieGenre>();
}

public class MovieGenre
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MovieId { get; set; }
    public Guid GenreId { get; set; }

    // Foreign Keys
    public Movie? Movie { get; set; }
    public Genre? Genre { get; set; }
}
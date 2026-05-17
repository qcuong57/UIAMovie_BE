namespace UIAMovie.Domain.Entities;

public class TvShow
{
    public Guid    Id               { get; set; } = Guid.NewGuid();
    public int?    TmdbId           { get; set; }
    public string  Title            { get; set; } = string.Empty;
    public string  Description      { get; set; } = string.Empty;
    public string? PosterUrl        { get; set; }
    public string? BackdropUrl      { get; set; }
    public decimal? ImdbRating      { get; set; }
    public string? OriginCountry    { get; set; }
    public string? Status           { get; set; }  // "Returning Series", "Ended", "Canceled"
    public int?    NumberOfSeasons  { get; set; }
    public int?    NumberOfEpisodes { get; set; }
    public int?    EpisodeRuntime   { get; set; }  // phút / tập
    public DateTime? FirstAirDate   { get; set; }
    public DateTime? LastAirDate    { get; set; }
    public bool    IsPublished      { get; set; } = true;
    /// <summary>TRUE = chỉ dành cho user Premium. Mặc định false (free).</summary>
    public bool    IsPremium        { get; set; } = false;

    // Navigation
    public ICollection<TvShowGenre>        TvShowGenres        { get; set; } = new List<TvShowGenre>();
    public ICollection<Season>             Seasons             { get; set; } = new List<Season>();
    public ICollection<TvShowCast>         TvShowCasts         { get; set; } = new List<TvShowCast>();
    public ICollection<TvShowDirector>     TvShowDirectors     { get; set; } = new List<TvShowDirector>();
    public ICollection<TvShowImage>        TvShowImages        { get; set; } = new List<TvShowImage>();
    public ICollection<TvShowVideo>        TvShowVideos        { get; set; } = new List<TvShowVideo>();
    public ICollection<TvShowWatchHistory> TvShowWatchHistories { get; set; } = new List<TvShowWatchHistory>();
}

public class TvShowGenre
{
    public Guid    Id       { get; set; } = Guid.NewGuid();
    public Guid    TvShowId { get; set; }
    public Guid    GenreId  { get; set; }
 
    public TvShow TvShow { get; set; } = null!;
    public Genre  Genre  { get; set; } = null!;
}
 
// ── TvShowCast — giống MovieCast, FK → TvShowId ──────────────────────────────
 
public class TvShowCast
{
    public Guid   Id        { get; set; } = Guid.NewGuid();
    public Guid   TvShowId  { get; set; }
    public Guid   PersonId  { get; set; }
    public string Character { get; set; } = string.Empty;
    public int    Order     { get; set; }
 
    public TvShow TvShow { get; set; } = null!;
    public Person Person { get; set; } = null!;
}
 
// ── TvShowDirector — giống MovieDirector, FK → TvShowId ──────────────────────
 
public class TvShowDirector
{
    public Guid TvShowId { get; set; }
    public Guid PersonId { get; set; }
    public Guid Id       { get; set; } = Guid.NewGuid();
 
    public TvShow TvShow { get; set; } = null!;
    public Person Person { get; set; } = null!;
}
 
// ── TvShowImage — giống MovieImage, FK → TvShowId ────────────────────────────
 
public class TvShowImage
{
    public Guid   Id        { get; set; } = Guid.NewGuid();
    public Guid   TvShowId  { get; set; }
    public string Url       { get; set; } = string.Empty;
    /// <summary>"backdrop" | "poster"</summary>
    public string ImageType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
 
    public TvShow TvShow { get; set; } = null!;
}
 
// ── TvShowVideo — giống MovieVideo, FK → TvShowId ────────────────────────────
 
public class TvShowVideo
{
    public Guid    Id        { get; set; } = Guid.NewGuid();
    public Guid    TvShowId  { get; set; }
    public string  VideoUrl  { get; set; } = string.Empty;
    /// <summary>"trailer" | "teaser" | "clip"</summary>
    public string  VideoType { get; set; } = string.Empty;
    public int?    Duration  { get; set; }
    public string? Quality   { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
 
    public TvShow TvShow { get; set; } = null!;
}

public class TvShowFavorite
{
    public Guid Id       { get; set; } = Guid.NewGuid();
    public Guid UserId   { get; set; }
    public Guid TvShowId { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
 
    // Navigation
    public TvShow? TvShow { get; set; }
}

// ── TvShowWatchHistory — track tiến độ xem của user ──────────────────────────

public class TvShowWatchHistory
{
    public Guid     Id              { get; set; } = Guid.NewGuid();
    public Guid     UserId          { get; set; }
    public Guid     TvShowId        { get; set; }
    /// <summary>
    /// null nếu track ở level show (đã xem tập nào đó nhưng không rõ tập).
    /// Có giá trị nếu track từng episode cụ thể.
    /// </summary>
    public Guid?    EpisodeId       { get; set; }
    public DateTime WatchedAt       { get; set; } = DateTime.UtcNow;
    public int      ProgressSeconds { get; set; }
    public bool     IsCompleted     { get; set; }

    // Navigation
    public TvShow?  TvShow   { get; set; }
    public Episode? Episode  { get; set; }
}
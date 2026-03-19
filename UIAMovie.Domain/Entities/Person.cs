namespace UIAMovie.Domain.Entities;

/// <summary>
/// Đại diện cho một người — diễn viên hoặc đạo diễn.
/// TmdbPersonId dùng để UPSERT tránh duplicate khi import nhiều phim.
/// </summary>
public class Person
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? TmdbPersonId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ProfileUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<MovieCast>     MovieCasts     { get; set; } = new List<MovieCast>();
    public ICollection<MovieDirector> MovieDirectors { get; set; } = new List<MovieDirector>();
}

// ─────────────────────────────────────────────────────────────────────────────

public class MovieCast
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MovieId { get; set; }
    public Guid PersonId { get; set; }
    public string Character { get; set; } = string.Empty;  // tên nhân vật
    public int Order { get; set; }                          // thứ tự trong credits

    // FK
    public Movie?  Movie  { get; set; }
    public Person? Person { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────

public class MovieDirector
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MovieId  { get; set; }
    public Guid PersonId { get; set; }

    // FK
    public Movie?  Movie  { get; set; }
    public Person? Person { get; set; }
}
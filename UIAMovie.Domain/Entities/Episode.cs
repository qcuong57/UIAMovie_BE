namespace UIAMovie.Domain.Entities;

public class Episode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeasonId { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Overview { get; set; }
    public string? StillUrl { get; set; } // thumbnail từng tập
    public int? Runtime { get; set; } // phút
    public decimal? Rating { get; set; }
    public DateTime? AirDate { get; set; }
    public int? TmdbId { get; set; }
    public string? VideoUrl { get; set; } // URL video thực tế (Cloudinary)

    public Season Season { get; set; } = null!;
}
namespace UIAMovie.Domain.Entities;

public class Season
{
    public Guid    Id           { get; set; } = Guid.NewGuid();
    public Guid    TvShowId     { get; set; }
    public int     SeasonNumber { get; set; }
    public string? Name         { get; set; }
    public string? Overview     { get; set; }
    public string? PosterUrl    { get; set; }
    public DateTime? AirDate    { get; set; }
    public int     EpisodeCount { get; set; }
    public int?    TmdbId       { get; set; }

    public TvShow              TvShow   { get; set; } = null!;
    public ICollection<Episode> Episodes { get; set; } = new List<Episode>();
}
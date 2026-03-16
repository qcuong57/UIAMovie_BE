using Microsoft.AspNetCore.Http;

namespace UIAMovie.Application.DTOs;

public class MovieDTO
{
    public Guid Id { get; set; }       // ← Guid
    public string Title { get; set; }
    public string Description { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public int? Duration { get; set; }
    public decimal? Rating { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<MovieVideoDTO> Videos { get; set; } = new();
}

public class CreateMovieDTO
{
    public int? TmdbId { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public int? Duration { get; set; }
    public decimal? ImdbRating { get; set; }
    public string? ContentRating { get; set; }
    public List<Guid> GenreIds { get; set; } = new();  // ← Guid
}

public class UpdateMovieDTO
{
    public string Title { get; set; }
    public string Description { get; set; }
    public decimal? ImdbRating { get; set; }
}

public class MovieVideoDTO
{
    public Guid Id { get; set; }       // ← Guid
    public string VideoUrl { get; set; }
    public string VideoType { get; set; }
    public int? Duration { get; set; }
    public string? Quality { get; set; }
}

public class UploadMovieVideoDTO
{
    public Guid MovieId { get; set; }  // ← Guid
    public IFormFile VideoFile { get; set; }
    public string VideoType { get; set; }
    public string? Quality { get; set; }
}

public class TrendingMoviesDTO
{
    public List<MovieDTO> Movies { get; set; } = new();
    public int Total { get; set; }
}
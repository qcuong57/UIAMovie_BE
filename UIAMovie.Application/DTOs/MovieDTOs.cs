using Microsoft.AspNetCore.Http;

namespace UIAMovie.Application.DTOs;

public class MovieDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public int? Duration { get; set; }
    public decimal? Rating { get; set; }
    
    public List<string> Genres { get; set; } = new();
    public List<MovieVideoDTO> Videos { get; set; } = new();
    public string? TrailerKey { get; set; }

    // ← Mới: trả về khi GET detail
    public List<MovieCastDTO>  Cast      { get; set; } = new();
    public string?             Director  { get; set; }
    public List<MovieImageDTO> Images    { get; set; } = new();
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
    public List<Guid> GenreIds { get; set; } = new();

    // ← Mới: dùng khi import từ TMDB
    public List<ImportCastDTO>    Cast      { get; set; } = new();
    public ImportDirectorDTO?     Director  { get; set; }
    public List<ImportImageDTO>   Images    { get; set; } = new();
    public List<ImportTrailerDTO> Trailers  { get; set; } = new();
}

public class UpdateMovieDTO
{
    public string Title { get; set; }
    public string Description { get; set; }
    public decimal? ImdbRating { get; set; }
}

// ── Video DTOs ────────────────────────────────────────────────────────────────

public class MovieVideoDTO
{
    public Guid Id { get; set; }
    public string VideoUrl { get; set; }
    public string VideoType { get; set; }
    public int? Duration { get; set; }
    public string? Quality { get; set; }
}

public class UploadMovieVideoDTO
{
    public Guid MovieId { get; set; }
    public IFormFile VideoFile { get; set; }
    public string VideoType { get; set; }
    public string? Quality { get; set; }
}

public class TrendingMoviesDTO
{
    public List<MovieDTO> Movies { get; set; } = new();
    public int Total { get; set; }
}

// ── Cast / Director / Image DTOs (response) ───────────────────────────────────

public class MovieCastDTO
{
    public string Name      { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    public int    Order     { get; set; }
    public string? ProfileUrl { get; set; }
}

public class MovieImageDTO
{
    public string Url       { get; set; } = string.Empty;
    public string ImageType { get; set; } = string.Empty; // backdrop | poster
}

// ── Import DTOs (request — dùng trong CreateMovieDTO khi import TMDB) ─────────

public class ImportCastDTO
{
    public int    TmdbPersonId { get; set; }
    public string Name        { get; set; } = string.Empty;
    public string Character   { get; set; } = string.Empty;
    public int    Order       { get; set; }
    public string? ProfileUrl { get; set; }
}

public class ImportDirectorDTO
{
    public int    TmdbPersonId { get; set; }
    public string Name        { get; set; } = string.Empty;
    public string? ProfileUrl { get; set; }
}

public class ImportImageDTO
{
    public string Url       { get; set; } = string.Empty;
    public string ImageType { get; set; } = string.Empty; // backdrop | poster
}

public class ImportTrailerDTO
{
    public string YoutubeUrl { get; set; } = string.Empty;
    public string Name       { get; set; } = string.Empty;
}

// ── Filter / Favorites / History DTOs ────────────────────────────────────────

// public class FilterMoviesDTO
// {
//     public string? Search    { get; set; }
//     public Guid?   GenreId   { get; set; }
//     public int     Page      { get; set; } = 1;
//     public int     PageSize  { get; set; } = 20;
// }
//
// public class AddFavoriteDTO
// {
//     public Guid MovieId { get; set; }
// }
//
// public class UpdateWatchProgressDTO
// {
//     public Guid MovieId         { get; set; }
//     public int  ProgressMinutes { get; set; }
//     public bool IsCompleted     { get; set; }
// }
using Microsoft.AspNetCore.Http;

namespace UIAMovie.Application.DTOs;

public class MovieDTO
{
    public Guid      Id          { get; set; }
    public string    Title       { get; set; } = string.Empty;
    public string    Description { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public string?   PosterUrl   { get; set; }
    public string?   BackdropUrl { get; set; }
    public int?      Duration    { get; set; }
    public decimal?  Rating      { get; set; }

    public List<string>        Genres     { get; set; } = new();
    public List<MovieVideoDTO> Videos     { get; set; } = new();
    public string?             TrailerKey { get; set; }

    public List<MovieCastDTO>  Cast   { get; set; } = new();
    public List<MovieImageDTO> Images { get; set; } = new();

    // Tên đạo diễn — giữ nguyên để không breaking change
    public string?          Director       { get; set; }
    // Thông tin đầy đủ đạo diễn kèm tiểu sử
    public PersonDetailDTO? DirectorDetail { get; set; }
}

public class CreateMovieDTO
{
    public int?      TmdbId        { get; set; }
    public string    Title         { get; set; } = string.Empty;
    public string?   Description   { get; set; }
    public DateTime? ReleaseDate   { get; set; }
    public string?   PosterUrl     { get; set; }
    public string?   BackdropUrl   { get; set; }
    public int?      Duration      { get; set; }
    public decimal?  ImdbRating    { get; set; }
    public string?   ContentRating { get; set; }
    public List<Guid> GenreIds     { get; set; } = new();

    public List<ImportCastDTO>    Cast     { get; set; } = new();
    public ImportDirectorDTO?     Director { get; set; }
    public List<ImportImageDTO>   Images   { get; set; } = new();
    public List<ImportTrailerDTO> Trailers { get; set; } = new();
}

public class UpdateMovieDTO
{
    public string?  Title       { get; set; }
    public string?  Description { get; set; }
    public decimal? ImdbRating  { get; set; }
}

// ── Video DTOs ────────────────────────────────────────────────────────────────

public class MovieVideoDTO
{
    public Guid    Id        { get; set; }
    public string  VideoUrl  { get; set; } = string.Empty;
    public string  VideoType { get; set; } = string.Empty;
    public int?    Duration  { get; set; }
    public string? Quality   { get; set; }
}

public class UploadMovieVideoDTO
{
    public Guid       MovieId   { get; set; }
    public IFormFile? VideoFile { get; set; }
    public string     VideoType { get; set; } = string.Empty;
    public string?    Quality   { get; set; }
}

public class TrendingMoviesDTO
{
    public List<MovieDTO> Movies { get; set; } = new();
    public int Total { get; set; }
}

// ── Cast / Director / Image DTOs (response) ───────────────────────────────────

/// <summary>Thông tin diễn viên trả về trong MovieDTO — kèm tiểu sử</summary>
public class MovieCastDTO
{
    public string  Name         { get; set; } = string.Empty;
    public string  Character    { get; set; } = string.Empty;
    public int     Order        { get; set; }
    public string? ProfileUrl   { get; set; }
    public int?    TmdbPersonId { get; set; }
    public string? Biography    { get; set; }
    public string? Birthday     { get; set; }
    public string? PlaceOfBirth { get; set; }
    /// <summary>Danh sách ảnh profile lưu trong DB</summary>
    public List<string> ProfileImages { get; set; } = new();
}

/// <summary>Thông tin đầy đủ một người (dùng cho DirectorDetail)</summary>
public class PersonDetailDTO
{
    public string  Name         { get; set; } = string.Empty;
    public string? ProfileUrl   { get; set; }
    public int?    TmdbPersonId { get; set; }
    public string? Biography    { get; set; }
    public string? Birthday     { get; set; }
    public string? PlaceOfBirth { get; set; }
    /// <summary>Danh sách ảnh profile lưu trong DB</summary>
    public List<string> ProfileImages { get; set; } = new();
}

public class MovieImageDTO
{
    public string Url       { get; set; } = string.Empty;
    public string ImageType { get; set; } = string.Empty;
}

// ── Import DTOs (request — dùng trong CreateMovieDTO khi import TMDB) ─────────

public class ImportCastDTO
{
    public int     TmdbPersonId  { get; set; }
    public string  Name          { get; set; } = string.Empty;
    public string  Character     { get; set; } = string.Empty;
    public int     Order         { get; set; }
    public string? ProfileUrl    { get; set; }
    public string? Biography     { get; set; }
    public string? Birthday      { get; set; }
    public string? PlaceOfBirth  { get; set; }
    /// <summary>Danh sách URL ảnh profile từ TMDB — tối đa 5 ảnh</summary>
    public List<string> ProfileImages { get; set; } = new();
}

public class ImportDirectorDTO
{
    public int     TmdbPersonId  { get; set; }
    public string  Name          { get; set; } = string.Empty;
    public string? ProfileUrl    { get; set; }
    public string? Biography     { get; set; }
    public string? Birthday      { get; set; }
    public string? PlaceOfBirth  { get; set; }
    /// <summary>Danh sách URL ảnh profile từ TMDB — tối đa 5 ảnh</summary>
    public List<string> ProfileImages { get; set; } = new();
}

public class ImportImageDTO
{
    public string Url       { get; set; } = string.Empty;
    public string ImageType { get; set; } = string.Empty;
}

public class ImportTrailerDTO
{
    public string YoutubeUrl { get; set; } = string.Empty;
    public string Name       { get; set; } = string.Empty;
}
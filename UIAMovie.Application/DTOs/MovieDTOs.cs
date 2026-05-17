// UIAMovie.Application/DTOs/MovieDTOs.cs
// THÊM: IsPremium vào MovieDTO và CreateMovieDTO
// THÊM: Access (ContentAccessDTO) vào MovieDTO để frontend biết user có xem được không

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
    /// <summary>Mã quốc gia sản xuất — ISO 3166-1 alpha-2, VD: "US", "KR", "JP"</summary>
    public string?   OriginCountry { get; set; }

    /// <summary>
    /// TRUE = phim này chỉ dành cho user Premium.
    /// Frontend dùng để hiển thị badge "PREMIUM" trên poster.
    /// </summary>
    public bool IsPremium { get; set; }

    public List<string>        Genres     { get; set; } = new();
    public List<MovieVideoDTO> Videos     { get; set; } = new();
    public string?             TrailerKey { get; set; }

    public List<MovieCastDTO>  Cast   { get; set; } = new();
    public List<MovieImageDTO> Images { get; set; } = new();

    public string?          Director       { get; set; }
    public PersonDetailDTO? DirectorDetail { get; set; }

    /// <summary>
    /// Thông tin quyền truy cập của user hiện tại đối với phim này.
    /// NULL khi trả về từ list/search (để tối ưu performance).
    /// Chỉ có giá trị khi gọi GET /api/movies/{id} với JWT token.
    /// </summary>
    public ContentAccessDTO? Access { get; set; }
}

/// <summary>
/// DTO cho trending — kế thừa MovieDTO và bổ sung thông tin xu hướng.
/// Frontend dùng Views7d để hiển thị "🔥 1.2k lượt xem tuần này".
/// TrendingRank để hiển thị "#1 Trending".
/// </summary>
public class TrendingMovieDTO : MovieDTO
{
    /// <summary>Thứ hạng trending, bắt đầu từ 1.</summary>
    public int    TrendingRank  { get; set; }
    /// <summary>Lượt xem trong 7 ngày gần nhất.</summary>
    public int    Views7d       { get; set; }
    /// <summary>Lượt xem trong 30 ngày gần nhất.</summary>
    public int    Views30d      { get; set; }
    /// <summary>Score tổng hợp dùng để xếp hạng (để frontend debug nếu cần).</summary>
    public double TrendingScore { get; set; }
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
    /// <summary>Mã quốc gia sản xuất — ISO 3166-1 alpha-2, VD: "US", "KR"</summary>
    public string?   OriginCountry { get; set; }

    /// <summary>TRUE = phim này chỉ dành cho Premium user. Mặc định false (free).</summary>
    public bool IsPremium { get; set; } = false;

    public List<Guid> GenreIds { get; set; } = new();

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

    /// <summary>Cập nhật trạng thái Premium của phim. NULL = không thay đổi.</summary>
    public bool? IsPremium { get; set; }
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
    public List<TrendingMovieDTO> Movies { get; set; } = new();
    public int Total { get; set; }
}

// ── Cast / Director / Image DTOs (response) ───────────────────────────────────

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
    public List<string> ProfileImages { get; set; } = new();
}

public class PersonDetailDTO
{
    public string  Name         { get; set; } = string.Empty;
    public string? ProfileUrl   { get; set; }
    public int?    TmdbPersonId { get; set; }
    public string? Biography    { get; set; }
    public string? Birthday     { get; set; }
    public string? PlaceOfBirth { get; set; }
    public List<string> ProfileImages { get; set; } = new();
}

public class MovieImageDTO
{
    public string Url       { get; set; } = string.Empty;
    public string ImageType { get; set; } = string.Empty;
}

// ── Import DTOs ───────────────────────────────────────────────────────────────

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
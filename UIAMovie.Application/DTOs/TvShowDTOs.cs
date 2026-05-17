// UIAMovie.Application/DTOs/TvShowDTOs.cs

using Microsoft.AspNetCore.Http;

namespace UIAMovie.Application.DTOs;

// ── Primary response DTOs ─────────────────────────────────────────────────────

/// <summary>
/// DTO đầy đủ cho 1 TV show — trả về từ GET /api/tvshows/{id}.
/// </summary>
public class TvShowDTO
{
    public Guid      Id               { get; set; }
    public string    Title            { get; set; } = string.Empty;
    public string    Description      { get; set; } = string.Empty;
    public DateTime? FirstAirDate     { get; set; }
    public DateTime? LastAirDate      { get; set; }
    public string?   PosterUrl        { get; set; }
    public string?   BackdropUrl      { get; set; }
    /// <summary>Thời lượng trung bình mỗi tập (phút).</summary>
    public int?      EpisodeRuntime   { get; set; }
    public decimal?  Rating           { get; set; }
    /// <summary>ISO 3166-1 alpha-2, VD: "US", "KR", "JP"</summary>
    public string?   OriginCountry    { get; set; }
    /// <summary>"Returning Series" | "Ended" | "Canceled" | "In Production"</summary>
    public string?   Status           { get; set; }
    public int?      NumberOfSeasons  { get; set; }
    public int?      NumberOfEpisodes { get; set; }

    /// <summary>
    /// TRUE = TV show này chỉ dành cho user Premium.
    /// Frontend dùng để hiển thị badge "PREMIUM" trên poster.
    /// </summary>
    public bool IsPremium { get; set; }

    public List<string>         Genres     { get; set; } = new();
    public List<TvShowVideoDTO> Videos     { get; set; } = new();
    public string?              TrailerKey { get; set; }

    public List<TvShowCastDTO>  Cast           { get; set; } = new();
    public List<TvShowImageDTO> Images         { get; set; } = new();
    public string?              Director       { get; set; }
    public PersonDetailDTO?     DirectorDetail { get; set; }

    /// <summary>Danh sách season kèm episode — chỉ có khi gọi GetByIdWithDetailsAsync.</summary>
    public List<SeasonDTO> Seasons { get; set; } = new();

    /// <summary>
    /// Thông tin quyền truy cập của user hiện tại đối với TV show này.
    /// NULL khi trả về từ list/search (để tối ưu performance).
    /// Chỉ có giá trị khi gọi GET /api/tvshows/{id} với JWT token.
    /// </summary>
    public ContentAccessDTO? Access { get; set; }
}

/// <summary>
/// Season DTO — bao gồm danh sách episode.
/// </summary>
public class SeasonDTO
{
    public Guid      Id           { get; set; }
    public int       SeasonNumber { get; set; }
    public string?   Name         { get; set; }
    public string?   Overview     { get; set; }
    public string?   PosterUrl    { get; set; }
    public DateTime? AirDate      { get; set; }
    public int       EpisodeCount { get; set; }

    public List<EpisodeDTO> Episodes { get; set; } = new();
}

/// <summary>
/// Episode DTO — 1 tập cụ thể.
/// </summary>
public class EpisodeDTO
{
    public Guid      Id            { get; set; }
    public int       EpisodeNumber { get; set; }
    public string    Title         { get; set; } = string.Empty;
    public string?   Overview      { get; set; }
    public string?   StillUrl      { get; set; }
    public int?      Runtime       { get; set; }
    public decimal?  Rating        { get; set; }
    public DateTime? AirDate       { get; set; }
    /// <summary>URL video thực tế (Cloudinary). null nếu chưa upload.</summary>
    public string?   VideoUrl      { get; set; }
}

/// <summary>
/// TvShow rút gọn — dùng cho list/paged response (không kèm Season/Episode).
/// </summary>
public class TvShowSummaryDTO
{
    public Guid      Id               { get; set; }
    public string    Title            { get; set; } = string.Empty;
    public string    Description      { get; set; } = string.Empty;
    public DateTime? FirstAirDate     { get; set; }
    public string?   PosterUrl        { get; set; }
    public string?   BackdropUrl      { get; set; }
    public decimal?  Rating           { get; set; }
    public string?   OriginCountry    { get; set; }
    public string?   Status           { get; set; }
    public int?      NumberOfSeasons  { get; set; }
    public int?      NumberOfEpisodes { get; set; }
    public string?   TrailerKey       { get; set; }
    /// <summary>
    /// TRUE = TV show này chỉ dành cho user Premium.
    /// Frontend dùng để hiển thị badge "PREMIUM" trên poster.
    /// </summary>
    public bool      IsPremium        { get; set; }
    public List<string> Genres        { get; set; } = new();
}

// ── Create / Update DTOs ──────────────────────────────────────────────────────

public class CreateTvShowDTO
{
    public int?      TmdbId           { get; set; }
    public string    Title            { get; set; } = string.Empty;
    public string?   Description      { get; set; }
    public DateTime? FirstAirDate     { get; set; }
    public DateTime? LastAirDate      { get; set; }
    public string?   PosterUrl        { get; set; }
    public string?   BackdropUrl      { get; set; }
    public int?      EpisodeRuntime   { get; set; }
    public decimal?  ImdbRating       { get; set; }
    public string?   ContentRating    { get; set; }
    public string?   OriginCountry    { get; set; }
    public string?   Status           { get; set; }
    public int?      NumberOfSeasons  { get; set; }
    public int?      NumberOfEpisodes { get; set; }

    /// <summary>TRUE = TV show này chỉ dành cho Premium user. Mặc định false (free).</summary>
    public bool IsPremium { get; set; } = false;

    public List<Guid>              GenreIds { get; set; } = new();
    public List<ImportCastDTO>     Cast     { get; set; } = new();
    public ImportDirectorDTO?      Director { get; set; }
    public List<ImportImageDTO>    Images   { get; set; } = new();
    public List<ImportTrailerDTO>  Trailers { get; set; } = new();

    /// <summary>
    /// Key = SeasonNumber → season data kèm episodes.
    /// Chỉ import season có SeasonNumber > 0 (bỏ Specials).
    /// </summary>
    public List<CreateSeasonDTO> Seasons { get; set; } = new();
}

public class CreateSeasonDTO
{
    public int       SeasonNumber { get; set; }
    public string?   Name         { get; set; }
    public string?   Overview     { get; set; }
    public string?   PosterUrl    { get; set; }
    public DateTime? AirDate      { get; set; }
    public List<CreateEpisodeDTO> Episodes { get; set; } = new();
}

public class CreateEpisodeDTO
{
    public int       EpisodeNumber { get; set; }
    public string    Title         { get; set; } = string.Empty;
    public string?   Overview      { get; set; }
    public string?   StillUrl      { get; set; }
    public int?      Runtime       { get; set; }
    public decimal?  Rating        { get; set; }
    public DateTime? AirDate       { get; set; }
}

public class UpdateTvShowDTO
{
    public string?  Title       { get; set; }
    public string?  Description { get; set; }
    public decimal? ImdbRating  { get; set; }
    public string?  Status      { get; set; }
    /// <summary>Cập nhật trạng thái Premium của TV show. NULL = không thay đổi.</summary>
    public bool?    IsPremium   { get; set; }
}

// ── Filter DTO ────────────────────────────────────────────────────────────────

/// <summary>
/// Filter cho GET /api/tvshows — tương tự FilterMoviesDTO.
/// </summary>
public class FilterTvShowsDTO
{
    public string?      Search        { get; set; }
    public List<Guid>?  GenreIds      { get; set; }
    public decimal?     MinRating     { get; set; }
    public decimal?     MaxRating     { get; set; }
    public DateTime?    FromFirstAirDate { get; set; }
    public DateTime?    ToFirstAirDate   { get; set; }
    public string?      OriginCountry { get; set; }
    public string?      Status        { get; set; }
    public string?      SortBy        { get; set; } = "rating";
    public bool         SortDesc      { get; set; } = true;
    public int          Page          { get; set; } = 1;
    public int          PageSize      { get; set; } = 20;

    /// <summary>
    /// Khi có danh sách Ids (AI recommend / search by Ids),
    /// bỏ qua tất cả filter khác và giữ thứ tự list.
    /// </summary>
    public List<Guid>?  Ids { get; set; }
}

// ── Video / Image DTOs ────────────────────────────────────────────────────────

public class TvShowVideoDTO
{
    public Guid    Id        { get; set; }
    public string  VideoUrl  { get; set; } = string.Empty;
    public string  VideoType { get; set; } = string.Empty;
    public int?    Duration  { get; set; }
    public string? Quality   { get; set; }
}

public class TvShowImageDTO
{
    public string Url       { get; set; } = string.Empty;
    public string ImageType { get; set; } = string.Empty;
}

// ── Cast DTO ──────────────────────────────────────────────────────────────────

public class TvShowCastDTO
{
    public string  Name          { get; set; } = string.Empty;
    public string  Character     { get; set; } = string.Empty;
    public int     Order         { get; set; }
    public string? ProfileUrl    { get; set; }
    public int?    TmdbPersonId  { get; set; }
    public string? Biography     { get; set; }
    public string? Birthday      { get; set; }
    public string? PlaceOfBirth  { get; set; }
    public List<string> ProfileImages { get; set; } = new();
}

public class SyncResultDTO
{
    public bool        Success            { get; set; }
    public int         NewEpisodes        { get; set; }
    public int         NewSeasons         { get; set; }
    public string      Message            { get; set; } = string.Empty;
    /// <summary>
    /// Bug 4 fix: season numbers whose server-side cache was invalidated during this sync.
    /// Frontend SeasonAccordion must reset loaded=false for any season whose number
    /// appears in this list, forcing a re-fetch instead of serving its stale snapshot.
    /// </summary>
    public List<int>   InvalidatedSeasons { get; set; } = new();
}

public class TvShowWatchHistoryDTO
{
    public Guid      Id              { get; set; }
    public Guid      TvShowId        { get; set; }
    public string    TvShowTitle     { get; set; } = string.Empty;
    public string?   PosterUrl       { get; set; }
    /// <summary>null nếu track ở level show, có giá trị nếu track từng episode.</summary>
    public Guid?     EpisodeId       { get; set; }
    public DateTime  WatchedAt       { get; set; }
    public int       ProgressSeconds { get; set; }
    public bool      IsCompleted     { get; set; }
}

/// <summary>
/// Request body cho POST /api/tvshows/history.
/// </summary>
public class UpdateTvShowWatchProgressDTO
{
    public Guid  TvShowId        { get; set; }
    /// <summary>null nếu track ở level show.</summary>
    public Guid? EpisodeId       { get; set; }
    public int   ProgressSeconds { get; set; }
    public bool  IsCompleted     { get; set; }
}

// ── Video Upload ───────────────────────────────────────────────────────────────

/// <summary>
/// Request body (multipart/form-data) cho POST /api/tvshows/{id}/videos.
/// </summary>
public class UploadTvShowVideoDTO
{
    public Guid       TvShowId  { get; set; }
    public IFormFile? VideoFile { get; set; }
    public string     VideoType { get; set; } = string.Empty;
    public string?    Quality   { get; set; }
}
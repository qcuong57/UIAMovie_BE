// UIAMovie.Application/DTOs/TvShowDTOs.cs

using Microsoft.AspNetCore.Http;

namespace UIAMovie.Application.DTOs;

// ── Primary response DTOs ─────────────────────────────────────────────────────

/// <summary>
/// DTO đầy đủ cho 1 TV show — trả về từ GET /api/tvshows/{id}.
/// </summary>
public class TvShowDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? FirstAirDate { get; set; }
    public DateTime? LastAirDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }

    /// <summary>Thời lượng trung bình mỗi tập (phút).</summary>
    public int? EpisodeRuntime { get; set; }

    public decimal? Rating { get; set; }

    /// <summary>ISO 3166-1 alpha-2, VD: "US", "KR", "JP"</summary>
    public string? OriginCountry { get; set; }

    /// <summary>"Returning Series" | "Ended" | "Canceled" | "In Production"</summary>
    public string? Status { get; set; }

    public int? NumberOfSeasons { get; set; }
    public int? NumberOfEpisodes { get; set; }

    /// <summary>
    /// TRUE = TV show này chỉ dành cho user Premium.
    /// Frontend dùng để hiển thị badge "PREMIUM" trên poster.
    /// </summary>
    public bool IsPremium { get; set; }

    public List<string> Genres { get; set; } = new();
    public List<TvShowVideoDTO> Videos { get; set; } = new();
    public string? TrailerKey { get; set; }

    /// <summary>
    /// URL video trailer tự upload lên Cloudinary (VideoType="trailer_upload").
    /// Chạy SONG SONG với TrailerKey (Youtube) — show có thể có cả 2, chỉ 1, hoặc không có.
    /// </summary>
    public string? TrailerVideoUrl { get; set; }

    public List<TvShowCastDTO> Cast { get; set; } = new();
    public List<TvShowImageDTO> Images { get; set; } = new();
    public string? Director { get; set; }
    public PersonDetailDTO? DirectorDetail { get; set; }

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
    public Guid Id { get; set; }
    public int SeasonNumber { get; set; }
    public string? Name { get; set; }
    public string? Overview { get; set; }
    public string? PosterUrl { get; set; }
    public DateTime? AirDate { get; set; }
    public int EpisodeCount { get; set; }

    public List<EpisodeDTO> Episodes { get; set; } = new();
}

/// <summary>
/// Episode DTO — 1 tập cụ thể.
/// </summary>
public class EpisodeDTO
{
    public Guid Id { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Overview { get; set; }
    public string? StillUrl { get; set; }
    public int? Runtime { get; set; }
    public decimal? Rating { get; set; }
    public DateTime? AirDate { get; set; }

    /// <summary>URL video thực tế (Cloudinary). null nếu chưa upload.</summary>
    public string? VideoUrl { get; set; }
}

/// <summary>
/// Body cho PUT /api/tvshows/{id}/seasons/{seasonNumber} — sửa thông tin 1 season
/// đã tồn tại (tiêu đề, mô tả, poster, ngày phát sóng). NULL = giữ nguyên giá trị cũ,
/// "" = xóa giá trị hiện tại (VD: xóa poster). Không dùng để tạo season mới hay
/// thêm/xóa episode — việc đó nằm ở SaveSeasonsAsync (lúc tạo TV show) hoặc
/// SyncNewEpisodesAsync (đồng bộ từ TMDB).
/// </summary>
public class UpdateSeasonDTO
{
    /// <summary>Tên season, VD: "Season 1". NULL = không đổi.</summary>
    public string? Name { get; set; }

    /// <summary>NULL = không đổi. "" = xóa mô tả hiện tại.</summary>
    public string? Overview { get; set; }

    /// <summary>Poster của season. NULL = không đổi. "" = xóa poster hiện tại.</summary>
    public string? PosterUrl { get; set; }

    public DateTime? AirDate { get; set; }
}

/// <summary>
/// Body cho PUT /api/tvshows/episodes/{episodeId} — sửa thông tin 1 episode đã tồn
/// tại (tiêu đề, mô tả, ảnh still, thời lượng, rating, ngày phát sóng). NULL = giữ
/// nguyên giá trị cũ, "" = xóa giá trị hiện tại (VD: xóa StillUrl). Không sửa
/// VideoUrl ở đây — dùng SetEpisodeVideoAsync / RemoveEpisodeVideoAsync riêng.
/// </summary>
public class UpdateEpisodeDTO
{
    /// <summary>NULL = không đổi. Không cho phép rỗng (episode luôn cần tiêu đề).</summary>
    public string? Title { get; set; }

    /// <summary>NULL = không đổi. "" = xóa mô tả hiện tại.</summary>
    public string? Overview { get; set; }

    /// <summary>Ảnh still (thumbnail) của episode. NULL = không đổi. "" = xóa ảnh hiện tại.</summary>
    public string? StillUrl { get; set; }

    public int? Runtime { get; set; }
    public decimal? Rating { get; set; }
    public DateTime? AirDate { get; set; }
}


public class TvShowSummaryDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? FirstAirDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public decimal? Rating { get; set; }
    public string? OriginCountry { get; set; }
    public string? Status { get; set; }
    public int? NumberOfSeasons { get; set; }
    public int? NumberOfEpisodes { get; set; }
    public string? TrailerKey { get; set; }
    public string? TrailerVideoUrl { get; set; }

    /// <summary>
    /// TRUE = TV show này chỉ dành cho user Premium.
    /// Frontend dùng để hiển thị badge "PREMIUM" trên poster.
    /// </summary>
    public bool IsPremium { get; set; }

    public List<string> Genres { get; set; } = new();
}

// ── Create / Update DTOs ──────────────────────────────────────────────────────

/// <summary>
/// Body cho POST /api/tvshows — dùng chung cho cả import TMDB và thêm thủ công
/// (xem TvShowService.CreateTvShowAsync). Cấu trúc field cố tình song song với
/// CreateMovieDTO để 2 luồng admin "thêm phim" / "thêm TV show" thao tác giống nhau;
/// khác biệt là các field đặc thù chuỗi phim (FirstAirDate/LastAirDate/Status/
/// NumberOfSeasons/NumberOfEpisodes/Seasons) thay cho ReleaseDate/Duration của Movie.
/// </summary>
public class CreateTvShowDTO
{
    /// <summary>NULL khi tạo thủ công (không qua TMDB). Có giá trị khi import từ TMDB.</summary>
    public int? TmdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? FirstAirDate { get; set; }
    public DateTime? LastAirDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }

    /// <summary>Thời lượng trung bình mỗi tập (phút).</summary>
    public int? EpisodeRuntime { get; set; }
    public decimal? ImdbRating { get; set; }
    public string? ContentRating { get; set; }

    /// <summary>Mã quốc gia sản xuất — ISO 3166-1 alpha-2, VD: "US", "KR", "JP"</summary>
    public string? OriginCountry { get; set; }

    /// <summary>"Returning Series" | "Ended" | "Canceled" | "In Production"</summary>
    public string? Status { get; set; }
    public int? NumberOfSeasons { get; set; }
    public int? NumberOfEpisodes { get; set; }

    /// <summary>TRUE = TV show này chỉ dành cho Premium user. Mặc định false (free).</summary>
    public bool IsPremium { get; set; } = false;

    public List<Guid> GenreIds { get; set; } = new();

    /// <summary>
    /// Diễn viên. Mỗi phần tử có thể là Person đã chọn từ dropdown (PersonId có giá trị)
    /// hoặc nhập tay (PersonId = null, match/tạo theo Name) — giống CreateMovieDTO.Cast.
    /// </summary>
    public List<ImportCastDTO> Cast { get; set; } = new();

    /// <summary>Đạo diễn. PersonId = null + Name rỗng ("") = không có đạo diễn.</summary>
    public ImportDirectorDTO? Director { get; set; }
    public List<ImportImageDTO> Images { get; set; } = new();
    public List<ImportTrailerDTO> Trailers { get; set; } = new();

    /// <summary>
    /// Danh sách season kèm episode. Chỉ những season có SeasonNumber > 0 được lưu
    /// (SeasonNumber = 0 là "Specials" trên TMDB, bị bỏ qua — xem SaveSeasonsAsync).
    /// Với luồng thêm thủ công, admin có thể gửi season/episode tự đặt số bất kỳ.
    /// </summary>
    public List<CreateSeasonDTO> Seasons { get; set; } = new();
}

/// <summary>Season gửi lên khi tạo TV show (thủ công hoặc import) — 1 season kèm danh sách episode.</summary>
public class CreateSeasonDTO
{
    public int SeasonNumber { get; set; }
    public string? Name { get; set; }
    public string? Overview { get; set; }
    public string? PosterUrl { get; set; }
    public DateTime? AirDate { get; set; }
    public List<CreateEpisodeDTO> Episodes { get; set; } = new();
}

/// <summary>Episode gửi lên khi tạo TV show. VideoUrl KHÔNG có ở đây — video được upload riêng sau khi tạo show, qua POST /api/tvshows/{id}/videos hoặc endpoint episode-video (xem SetEpisodeVideoAsync).</summary>
public class CreateEpisodeDTO
{
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Overview { get; set; }
    public string? StillUrl { get; set; }
    public int? Runtime { get; set; }
    public decimal? Rating { get; set; }
    public DateTime? AirDate { get; set; }
}

public class UpdateTvShowDTO
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public decimal? ImdbRating { get; set; }
    public string? Status { get; set; }

    /// <summary>Cập nhật trạng thái Premium của TV show. NULL = không thay đổi.</summary>
    public bool? IsPremium { get; set; }

    /// <summary>Poster mới. NULL = không thay đổi. "" = xóa poster hiện tại.</summary>
    public string? PosterUrl { get; set; }

    /// <summary>Backdrop (ảnh bìa chính) mới. NULL = không thay đổi. "" = xóa backdrop hiện tại.</summary>
    public string? BackdropUrl { get; set; }

    /// <summary>
    /// Danh sách diễn viên mới (thay thế toàn bộ cast hiện có).
    /// NULL = không thay đổi cast. [] (rỗng) = xóa hết diễn viên.
    /// Mỗi phần tử có thể là Person đã chọn từ dropdown (PersonId) hoặc
    /// diễn viên nhập tay (PersonId = null, match/tạo theo Name).
    /// </summary>
    public List<ImportCastDTO>? Cast { get; set; }

    /// <summary>
    /// Đạo diễn mới (thay thế đạo diễn hiện có).
    /// NULL = không thay đổi. Gửi object rỗng (Name = "") để xóa đạo diễn.
    /// </summary>
    public ImportDirectorDTO? Director { get; set; }

    /// <summary>
    /// Danh sách thể loại mới (thay thế toàn bộ thể loại hiện có).
    /// NULL = không thay đổi. [] (rỗng) = xóa hết thể loại.
    /// </summary>
    public List<Guid>? GenreIds { get; set; }

    /// <summary>
    /// Danh sách ảnh backdrop (gallery, tab "Hình ảnh") — thay thế toàn bộ backdrop hiện có.
    /// NULL = không thay đổi. [] (rỗng) = xóa hết ảnh backdrop.
    /// Không ảnh hưởng tới các ImageType khác (VD poster gallery, nếu có).
    /// </summary>
    public List<ImportImageDTO>? BackdropImages { get; set; }
}

// ── Filter DTO ────────────────────────────────────────────────────────────────

/// <summary>
/// Filter cho GET /api/tvshows — tương tự FilterMoviesDTO.
/// </summary>
public class FilterTvShowsDTO
{
    public string? Search { get; set; }
    public List<Guid>? GenreIds { get; set; }
    public decimal? MinRating { get; set; }
    public decimal? MaxRating { get; set; }
    public DateTime? FromFirstAirDate { get; set; }
    public DateTime? ToFirstAirDate { get; set; }
    public string? OriginCountry { get; set; }
    public string? Status { get; set; }
    public string? SortBy { get; set; } = "rating";
    public bool SortDesc { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Khi có danh sách Ids (AI recommend / search by Ids),
    /// bỏ qua tất cả filter khác và giữ thứ tự list.
    /// </summary>
    public List<Guid>? Ids { get; set; }
}

// ── Video / Image DTOs ────────────────────────────────────────────────────────

public class TvShowVideoDTO
{
    public Guid Id { get; set; }
    public string VideoUrl { get; set; } = string.Empty;
    public string VideoType { get; set; } = string.Empty;
    public int? Duration { get; set; }
    public string? Quality { get; set; }
}

public class TvShowImageDTO
{
    public string Url { get; set; } = string.Empty;
    public string ImageType { get; set; } = string.Empty;
}

// ── Cast DTO ──────────────────────────────────────────────────────────────────

public class TvShowCastDTO
{
    public string Name { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    public int Order { get; set; }
    public string? ProfileUrl { get; set; }
    public int? TmdbPersonId { get; set; }
    public string? Biography { get; set; }
    public string? Birthday { get; set; }
    public string? PlaceOfBirth { get; set; }
    public List<string> ProfileImages { get; set; } = new();
}

public class SyncResultDTO
{
    public bool Success { get; set; }
    public int NewEpisodes { get; set; }
    public int NewSeasons { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Bug 4 fix: season numbers whose server-side cache was invalidated during this sync.
    /// Frontend SeasonAccordion must reset loaded=false for any season whose number
    /// appears in this list, forcing a re-fetch instead of serving its stale snapshot.
    /// </summary>
    public List<int> InvalidatedSeasons { get; set; } = new();
}

/// <summary>
/// Request body cho POST /api/tvshows/history.
/// </summary>
public class UpdateTvShowWatchProgressDTO
{
    public Guid TvShowId { get; set; }

    /// <summary>null nếu track ở level show.</summary>
    public Guid? EpisodeId { get; set; }

    public int ProgressSeconds { get; set; }
    public bool IsCompleted { get; set; }
}

// ── Video Upload ───────────────────────────────────────────────────────────────

/// <summary>
/// Request body (multipart/form-data) cho POST /api/tvshows/{id}/videos.
/// </summary>
public class UploadTvShowVideoDTO
{
    public Guid TvShowId { get; set; }
    public IFormFile? VideoFile { get; set; }
    public string VideoType { get; set; } = string.Empty;
    public string? Quality { get; set; }
}
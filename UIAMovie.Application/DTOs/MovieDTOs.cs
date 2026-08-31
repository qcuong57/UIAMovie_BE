// UIAMovie.Application/DTOs/MovieDTOs.cs
// THÊM: IsPremium vào MovieDTO và CreateMovieDTO
// THÊM: Access (ContentAccessDTO) vào MovieDTO để frontend biết user có xem được không

using Microsoft.AspNetCore.Http;

namespace UIAMovie.Application.DTOs;

public class MovieDTO
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public int? Duration { get; set; }
    public decimal? Rating { get; set; }

    /// <summary>Mã quốc gia sản xuất — ISO 3166-1 alpha-2, VD: "US", "KR", "JP"</summary>
    public string? OriginCountry { get; set; }

    /// <summary>
    /// TRUE = phim này chỉ dành cho user Premium.
    /// Frontend dùng để hiển thị badge "PREMIUM" trên poster.
    /// </summary>
    public bool IsPremium { get; set; }

    public List<string> Genres { get; set; } = new();
    public List<MovieVideoDTO> Videos { get; set; } = new();
    public string? TrailerKey { get; set; }

    /// <summary>
    /// URL video trailer tự upload lên Cloudinary (VideoType="trailer_upload").
    /// Chạy SONG SONG với TrailerKey (Youtube) — phim có thể có cả 2, hoặc chỉ 1, hoặc không có.
    /// FE dùng field này để mở popup <video> thay vì popup iframe Youtube.
    /// </summary>
    public string? TrailerVideoUrl { get; set; }

    public List<MovieCastDTO> Cast { get; set; } = new();
    public List<MovieImageDTO> Images { get; set; } = new();

    public string? Director { get; set; }
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
    public int TrendingRank { get; set; }

    /// <summary>Lượt xem trong 7 ngày gần nhất.</summary>
    public int Views7d { get; set; }

    /// <summary>Lượt xem trong 30 ngày gần nhất.</summary>
    public int Views30d { get; set; }

    /// <summary>Score tổng hợp dùng để xếp hạng (để frontend debug nếu cần).</summary>
    public double TrendingScore { get; set; }
}

public class CreateMovieDTO
{
    public int? TmdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public int? Duration { get; set; }
    public decimal? ImdbRating { get; set; }
    public string? ContentRating { get; set; }

    /// <summary>Mã quốc gia sản xuất — ISO 3166-1 alpha-2, VD: "US", "KR"</summary>
    public string? OriginCountry { get; set; }

    /// <summary>TRUE = phim này chỉ dành cho Premium user. Mặc định false (free).</summary>
    public bool IsPremium { get; set; } = false;

    public List<Guid> GenreIds { get; set; } = new();

    public List<ImportCastDTO> Cast { get; set; } = new();
    public ImportDirectorDTO? Director { get; set; }
    public List<ImportImageDTO> Images { get; set; } = new();
    public List<ImportTrailerDTO> Trailers { get; set; } = new();
}

public class UpdateMovieDTO
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public decimal? ImdbRating { get; set; }

    /// <summary>Cập nhật trạng thái Premium của phim. NULL = không thay đổi.</summary>
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

/// <summary>DTO gọn dùng cho dropdown chọn diễn viên/đạo diễn đã có trong hệ thống.</summary>
public class PersonSearchDTO
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ProfileUrl { get; set; }
    public int? TmdbPersonId { get; set; }
}

// ── Video DTOs ────────────────────────────────────────────────────────────────

public class MovieVideoDTO
{
    public Guid Id { get; set; }
    public string VideoUrl { get; set; } = string.Empty;
    public string VideoType { get; set; } = string.Empty;
    public int? Duration { get; set; }
    public string? Quality { get; set; }
}

public class UploadMovieVideoDTO
{
    public Guid MovieId { get; set; }
    public IFormFile? VideoFile { get; set; }
    public string VideoType { get; set; } = string.Empty;
    public string? Quality { get; set; }
}

public class TrendingMoviesDTO
{
    public List<TrendingMovieDTO> Movies { get; set; } = new();
    public int Total { get; set; }
}

// ── Cast / Director / Image DTOs (response) ───────────────────────────────────

public class MovieCastDTO
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

public class PersonDetailDTO
{
    public string Name { get; set; } = string.Empty;
    public string? ProfileUrl { get; set; }
    public int? TmdbPersonId { get; set; }
    public string? Biography { get; set; }
    public string? Birthday { get; set; }
    public string? PlaceOfBirth { get; set; }
    public List<string> ProfileImages { get; set; } = new();
}

public class MovieImageDTO
{
    /// <summary>ID của MovieImage trong DB — FE dùng để xoá/định danh từng ảnh trong gallery.</summary>
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string ImageType { get; set; } = string.Empty;
}

// ── Import DTOs ───────────────────────────────────────────────────────────────

public class ImportCastDTO
{
    /// <summary>NULL khi diễn viên được thêm thủ công (không có trên TMDB) — Person sẽ được match/tạo theo Name.</summary>
    public int? TmdbPersonId { get; set; }
    public Guid? PersonId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    public int Order { get; set; }
    public string? ProfileUrl { get; set; }
    public string? Biography { get; set; }
    public string? Birthday { get; set; }
    public string? PlaceOfBirth { get; set; }
    public List<string> ProfileImages { get; set; } = new();
}

public class ImportDirectorDTO
{
    /// <summary>NULL khi đạo diễn được thêm thủ công (không có trên TMDB) — Person sẽ được match/tạo theo Name.</summary>
    public int? TmdbPersonId { get; set; }
    public Guid? PersonId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ProfileUrl { get; set; }
    public string? Biography { get; set; }
    public string? Birthday { get; set; }
    public string? PlaceOfBirth { get; set; }
    public List<string> ProfileImages { get; set; } = new();
}

public class ImportImageDTO
{
    public string Url { get; set; } = string.Empty;
    public string ImageType { get; set; } = string.Empty;
}

public class ImportTrailerDTO
{
    public string YoutubeUrl { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Body cho PUT /api/movies/{id}/trailer/youtube — admin set/đổi link trailer Youtube thủ công.</summary>
public class SetTrailerYoutubeDTO
{
    public string YoutubeUrl { get; set; } = string.Empty;
}
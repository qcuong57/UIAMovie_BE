using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Interfaces;

public interface IGroqService
{
    /// <summary>Chat thông thường về phim — hỗ trợ conversation history đa lượt</summary>
    Task<string> ChatAsync(
        string                userMessage,
        string?               systemContext = null,
        List<ChatMessageDTO>? history       = null);

    /// <summary>
    /// Gợi ý phim từ database thật — trả về danh sách Guid đã được rank.
    /// availableMovies: metadata đầy đủ để Groq hiểu ngữ cảnh phim.
    /// </summary>
    Task<List<Guid>> RecommendMoviesAsync(
        List<string>      watchedTitles,
        List<string>      favoriteGenres,
        List<MovieContext> availableMovies);

    /// <summary>
    /// AI-powered search: hiểu ngôn ngữ tự nhiên, trả về Guid của phim phù hợp nhất.
    /// Ví dụ: "phim kinh dị hay nhất 2020" / "phim có diễn viên Tom Hanks"
    /// </summary>
    Task<List<Guid>> SmartSearchAsync(string query, List<MovieContext> availableMovies);

    /// <summary>
    /// [v2] Gợi ý phim theo tâm trạng — trả về danh sách Guid đã được rank.
    /// mood: tâm trạng normalize (vd: "buồn", "vui", "hồi hộp").
    /// targetGenres: danh sách genre phù hợp, dạng CSV (vd: "Drama, Romance").
    /// movieCsv: danh sách phim dạng CSV để AI chọn từ đó.
    /// </summary>
    Task<List<Guid>> MoodRecommendAsync(
        string mood,
        string targetGenres,
        string movieCsv);

    // ─── TV Show ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Gợi ý TV show từ database — trả về danh sách Guid đã được rank.
    /// </summary>
    Task<List<Guid>> RecommendTvShowsAsync(
        List<string>        watchedTitles,
        List<string>        favoriteGenres,
        List<TvShowContext> availableShows);

    /// <summary>
    /// AI-powered search TV show — hiểu ngôn ngữ tự nhiên.
    /// Ví dụ: "phim bộ hàn lãng mạn" / "series nhiều mùa hay nhất"
    /// </summary>
    Task<List<Guid>> SmartSearchTvShowsAsync(string query, List<TvShowContext> availableShows);
}

/// <summary>Context phim gửi lên Groq — đủ metadata để AI hiểu, không thừa token</summary>
public record MovieContext(
    Guid   Id,
    string Title,
    string Genres,        // "Action, Thriller"
    double Rating,
    int?   Year,
    string Description    // 200 ký tự đầu
);

/// <summary>Context TV show gửi lên Groq — tương tự MovieContext, thêm NumberOfSeasons</summary>
public record TvShowContext(
    Guid   Id,
    string Title,
    string Genres,           // "Drama, Romance"
    double Rating,
    int?   Year,             // FirstAirDate?.Year
    string Description,      // 200 ký tự đầu
    int?   NumberOfSeasons
);
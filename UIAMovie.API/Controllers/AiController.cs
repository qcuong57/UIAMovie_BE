using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.AI;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Services;

namespace UIAMovie.Controllers;

/// <summary>
/// AI Controller — thin controller, logic nằm ở services.
///
/// Fixes so với phiên bản cũ:
///   [1] Chat: hỗ trợ conversation history — AI nhớ ngữ cảnh đa lượt
///   [2] Chat: filter phim theo context câu hỏi thay vì lấy top 30 cứng
///   [3] Recommend: truyền thêm WatchHistoryDTO đầy đủ để AI có tín hiệu chất lượng
///   [4] Recommend: loại phim đang xem dở ra khỏi gợi ý
///   [5] Cache invalidation key cho AI contexts khi phim mới được thêm/sửa
///   [NEW] Chat: detect câu hỏi về diễn viên/đạo diễn → fetch detail phim → inject cast/director vào context
///   [NEW] Chat: parse reply để trả kèm MovieDTO của các phim được mention → frontend hiện movie cards
///   [v2] Chat: intent routing — site/mood/compare → chuyển sang handler tương ứng
///   [v2] POST /api/ai/mood     — gợi ý phim theo tâm trạng (cache 15 phút)
///   [v2] POST /api/ai/compare  — so sánh 2 phim thành bảng 6 tiêu chí (cache 30 phút)
///   [v2] POST /api/ai/review   — tóm tắt đánh giá phim (wire Review endpoint)
///
/// Fixes [v3]:
///   [FIX-1] Chat: thêm input validation (message không rỗng, tối đa 500 ký tự)
///   [FIX-2] BuildMovieCsv: loại bỏ duplicate — dùng AiMovieCsvBuilder static helper chung
///   [FIX-3] DetectIntent: xử lý multi-intent — mood+compare ưu tiên mood nếu cả hai xuất hiện
///   [FIX-4] HandleMoodChatAsync: reply do AI sinh ra thay vì hardcode string
///   [FIX-5] FindTwoMoviesInMessage: fuzzy match — không yêu cầu exact title
///   [FIX-6] Rate limiting: SlidingWindowRateLimiter thay thế SemaphoreSlim đơn thuần
///   [FIX-7] Wire Review endpoint: POST /api/ai/review
///
/// [v4] TV Show support:
///   - Chat intent "tvshow": HandleTvShowChatAsync — chat về phim bộ/series
///   - GET /api/ai/recommend/tvshows — gợi ý series dựa trên lịch sử xem
///   - GET /api/ai/search/tvshows?q= — AI search series bằng ngôn ngữ tự nhiên
///   - GetCachedTvShowContextsAsync / GetAllTvShowDtosAsync — cache helpers riêng
///   - SelectTvShowsForChat / ExtractMentionedTvShows — helpers riêng cho series
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AiController : ControllerBase
{
    private readonly IGroqService        _groq;
    private readonly IMovieService       _movies;
    private readonly ITvShowService      _tvShows;
    private readonly IRatingReviewService _reviews;
    private readonly ICacheService       _cache;
    private readonly ILogger<AiController> _logger;

    // ── Cache keys ────────────────────────────────────────────────────────────
    private const string MovieContextsCacheKey  = "ai:movie_contexts";
    private const string AllMovieDtosCacheKey   = "ai:all_movie_dtos";
    private const string TvShowContextsCacheKey = "ai:tvshow_contexts";
    private const string AllTvShowDtosCacheKey  = "ai:all_tvshow_dtos";

    // TTL 30 phút — giảm tần suất query DB (Neon free tier: 128 max connections)
    private static readonly TimeSpan ContextCacheTtl = TimeSpan.FromMinutes(30);

    // ── Limits ────────────────────────────────────────────────────────────────
    private const int MaxQueryLength        = 200;
    private const int MaxChatMessageLength  = 500;
    private const int DescriptionMaxLength  = 200;
    private const int MovieFetchPageSize    = 500;
    private const int MaxHistoryTurns       = 20;
    private const int MaxMovieCards         = 6;

    // Chống thundering herd: chỉ 1 request được phép query DB khi cache miss
    private static readonly SemaphoreSlim _contextCacheLock    = new(1, 1);
    private static readonly SemaphoreSlim _dtoCacheLock        = new(1, 1);
    private static readonly SemaphoreSlim _tvShowContextLock   = new(1, 1);
    private static readonly SemaphoreSlim _tvShowDtoCacheLock  = new(1, 1);

    // Keywords để detect câu hỏi về cast/director
    private static readonly string[] CastKeywords =
    [
        "diễn viên", "đạo diễn", "actor", "actress", "director", "cast",
        "vai", "đóng", "ai đóng", "ai đạo diễn", "thủ vai", "nhân vật",
        "character", "star", "ngôi sao", "tên diễn", "ai trong"
    ];

    public AiController(
        IGroqService         groq,
        IMovieService        movies,
        ITvShowService       tvShows,
        IRatingReviewService reviews,
        ICacheService        cache,
        ILogger<AiController> logger)
    {
        _groq    = groq;
        _movies  = movies;
        _tvShows = tvShows;
        _reviews = reviews;
        _cache   = cache;
        _logger  = logger;
    }

    // ─── POST /api/ai/chat ────────────────────────────────────────────────────

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] AiChatRequestDTO dto)
    {
        // [FIX-1] Input validation — đồng nhất với các endpoint khác
        if (string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest(ApiResponse.Fail("Message không được để trống."));

        if (dto.Message.Length > MaxChatMessageLength)
            return BadRequest(ApiResponse.Fail($"Message không được vượt quá {MaxChatMessageLength} ký tự."));

        try
        {
            // Chỉ lấy turn của user — tránh assistant reply về series làm lệch intent
            // VD: assistant reply trước có "series", "season" → không được ảnh hưởng intent mới
            var recentHistory = dto.History
                .Where(h => h.Role == "user")
                .TakeLast(3)
                .Select(h => h.Content)
                .ToList();

            // [FIX-3] Multi-intent routing: DetectIntent trả về list ưu tiên
            var intent = MoviePrompts.DetectIntent(dto.Message, recentHistory);

            // ── Intent routing ────────────────────────────────────────────────
            return intent switch
            {
                "site"   => await HandleSiteChatAsync(dto),
                "mood"   => await HandleMoodChatAsync(dto),
                "compare"=> await HandleCompareChatAsync(dto),
                "review" => await HandleReviewChatAsync(dto),
                "tvshow" => await HandleTvShowChatAsync(dto),
                _        => await HandleMovieChatAsync(dto),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI] Chat failed, messageLength={Length}", dto.Message.Length);
            return StatusCode(503, ApiResponse.Fail("Dịch vụ AI tạm thời không khả dụng. Vui lòng thử lại."));
        }
    }

    // ─── POST /api/ai/mood ────────────────────────────────────────────────────

    /// <summary>
    /// Gợi ý phim theo tâm trạng.
    /// Body: { "mood": "buồn" }
    /// Cache key: "ai:mood:{normalizedMood}" — TTL 15 phút.
    /// </summary>
    [HttpPost("mood")]
    public async Task<IActionResult> MoodRecommend([FromBody] MoodRequestDTO dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Mood))
            return BadRequest(ApiResponse.Fail("Mood không được để trống."));

        var normalizedMood = dto.Mood.Trim().ToLowerInvariant();
        var cacheKey = $"ai:mood:{normalizedMood}";

        try
        {
            var cached = await _cache.GetAsync<MoodRecommendResultDTO>(cacheKey);
            if (cached != null)
                return Ok(ApiResponse.Ok(cached, $"Phim phù hợp với tâm trạng: {dto.Mood}"));

            var contexts = await GetCachedContextsAsync();

            // Lấy genres ưu tiên cho mood này
            var targetGenres = MoviePrompts.MoodGenreMap.TryGetValue(normalizedMood, out var genres)
                ? genres
                : [];

            // Filter + score phim theo genre match
            var subset = FilterMoviesForMood(contexts, targetGenres, limit: 20);
            var csv    = AiMovieCsvBuilder.Build(subset);

            var targetGenresStr = targetGenres.Length > 0
                ? string.Join(", ", targetGenres)
                : "any";

            var ids = await _groq.MoodRecommendAsync(normalizedMood, targetGenresStr, csv);

            List<MovieDTO> movies;
            if (ids.Count > 0)
            {
                var page = await _movies.GetMoviesAsync(
                    new FilterMoviesDTO { Ids = ids, PageSize = ids.Count });
                var map = page.Items.ToDictionary(m => m.Id);
                movies = ids.Where(map.ContainsKey).Select(id => map[id]).Take(9).ToList();
            }
            else
            {
                // Fallback: top phim theo genre match
                var allDtos = await GetAllMovieDtosAsync();
                movies = FallbackRecommend(allDtos, targetGenres.ToList(), take: 9);
            }

            var result = new MoodRecommendResultDTO
            {
                Mood   = dto.Mood,
                Movies = movies,
            };

            if (movies.Count > 0)
                await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15));

            return Ok(ApiResponse.Ok(result, $"Phim phù hợp với tâm trạng: {dto.Mood}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI] MoodRecommend failed for mood={Mood}", dto.Mood);
            return StatusCode(503, ApiResponse.Fail("Không thể lấy gợi ý lúc này. Vui lòng thử lại."));
        }
    }

    // ─── POST /api/ai/compare ─────────────────────────────────────────────────

    /// <summary>
    /// So sánh 2 phim, trả về bảng Markdown 6 tiêu chí.
    /// Body: { "movieIdA": "...", "movieIdB": "..." }
    /// Cache key: "ai:compare:{minId}:{maxId}" — bất kể thứ tự, TTL 30 phút.
    /// </summary>
    [HttpPost("compare")]
    public async Task<IActionResult> Compare([FromBody] CompareRequestDTO dto)
    {
        if (dto.MovieIdA == Guid.Empty || dto.MovieIdB == Guid.Empty)
            return BadRequest(ApiResponse.Fail("Cần cung cấp 2 ID phim hợp lệ."));

        if (dto.MovieIdA == dto.MovieIdB)
            return BadRequest(ApiResponse.Fail("Vui lòng chọn 2 phim khác nhau để so sánh."));

        // Bất kể A/B hay B/A → cùng cache key
        var cacheKey = BuildCompareCacheKey(dto.MovieIdA, dto.MovieIdB);

        try
        {
            var cached = await _cache.GetAsync<CompareResultDTO>(cacheKey);
            if (cached != null)
                return Ok(ApiResponse.Ok(cached, "Kết quả so sánh"));

            // Fetch song song 2 phim
            var (movieA, movieB) = await FetchMoviePairAsync(dto.MovieIdA, dto.MovieIdB);

            if (movieA is null)
                return NotFound(ApiResponse.Fail($"Không tìm thấy phim với ID: {dto.MovieIdA}"));
            if (movieB is null)
                return NotFound(ApiResponse.Fail($"Không tìm thấy phim với ID: {dto.MovieIdB}"));

            var userPrompt = MoviePrompts.BuildCompareUser(
                titleA:    movieA.Title,
                genresA:   string.Join(", ", movieA.Genres),
                ratingA:   (double)(movieA.Rating ?? 0),
                directorA: movieA.Director ?? "Không rõ",
                yearA:     movieA.ReleaseDate?.Year,
                descA:     TruncateDescription(movieA.Description),
                titleB:    movieB.Title,
                genresB:   string.Join(", ", movieB.Genres),
                ratingB:   (double)(movieB.Rating ?? 0),
                directorB: movieB.Director ?? "Không rõ",
                yearB:     movieB.ReleaseDate?.Year,
                descB:     TruncateDescription(movieB.Description));

            var markdownTable = await _groq.ChatAsync(userPrompt, MoviePrompts.CompareSystem);

            var result = new CompareResultDTO
            {
                MovieA        = movieA,
                MovieB        = movieB,
                MarkdownTable = markdownTable,
            };

            await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(30));

            return Ok(ApiResponse.Ok(result, "Kết quả so sánh"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI] Compare failed for {IdA} vs {IdB}", dto.MovieIdA, dto.MovieIdB);
            return StatusCode(503, ApiResponse.Fail("Không thể so sánh phim lúc này. Vui lòng thử lại."));
        }
    }

    // ─── POST /api/ai/review ──────────────────────────────────────────────────

    /// <summary>
    /// Tóm tắt đánh giá phim thành 1 câu tiếng Việt.
    /// Body: { "movieId": "..." }
    /// Cache key: "ai:review:{movieId}" — TTL 60 phút.
    /// </summary>
    [HttpPost("review")]
    public async Task<IActionResult> ReviewSummary([FromBody] ReviewRequestDTO dto)
    {
        if (dto.MovieId == Guid.Empty)
            return BadRequest(ApiResponse.Fail("MovieId không hợp lệ."));

        var cacheKey = $"ai:review:{dto.MovieId}";

        try
        {
            var cached = await _cache.GetAsync<ReviewSummaryResultDTO>(cacheKey);
            if (cached != null)
                return Ok(ApiResponse.Ok(cached, "Tóm tắt đánh giá"));

            var movie = await _movies.GetMovieByIdAsync(dto.MovieId);
            if (movie is null)
                return NotFound(ApiResponse.Fail($"Không tìm thấy phim với ID: {dto.MovieId}"));

            var reviews = await _reviews.GetMovieReviewsAsync(dto.MovieId);
            var reviewTexts = reviews
                .Where(r => !string.IsNullOrWhiteSpace(r.ReviewText))
                .Select(r => r.ReviewText!)
                .ToList();

            if (reviewTexts.Count == 0)
                return Ok(ApiResponse.Ok(
                    new ReviewSummaryResultDTO
                        { MovieId = dto.MovieId, Summary = "Chưa có đánh giá nào cho phim này." },
                    "Tóm tắt đánh giá"));

            var userPrompt = MoviePrompts.BuildReviewUser(movie.Title, reviewTexts);
            var summary    = await _groq.ChatAsync(userPrompt, MoviePrompts.ReviewSystem);

            var result = new ReviewSummaryResultDTO
            {
                MovieId = dto.MovieId,
                Summary = string.IsNullOrWhiteSpace(summary)
                    ? "Không thể tóm tắt đánh giá lúc này."
                    : summary,
            };

            await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(60));

            return Ok(ApiResponse.Ok(result, "Tóm tắt đánh giá"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI] ReviewSummary failed for movieId={MovieId}", dto.MovieId);
            return StatusCode(503, ApiResponse.Fail("Không thể tóm tắt đánh giá lúc này. Vui lòng thử lại."));
        }
    }

    // ─── GET /api/ai/recommend ────────────────────────────────────────────────

    [HttpGet("recommend")]
    [Authorize]
    public async Task<IActionResult> Recommend()
    {
        Guid userId;
        try
        {
            userId = GetUserId();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse.Fail(ex.Message));
        }

        try
        {
            var (history, contexts) = await FetchDataAsync(userId);

            var watchedIds      = history.Select(h => h.MovieId).ToHashSet();
            var watchedTitleSet = history.Select(h => h.MovieTitle).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var inProgressIds = history
                .Where(h => !h.IsCompleted && h.ProgressMinutes > 5)
                .Select(h => h.MovieId)
                .ToHashSet();

            var favoriteGenres = BuildFavoriteGenres(contexts, watchedIds, fallbackToAll: true);

            var unwatched = contexts
                .Where(m => !watchedTitleSet.Contains(m.Title) && !inProgressIds.Contains(m.Id))
                .ToList();

            var watchedTitles = history
                .OrderByDescending(h => h.IsCompleted ? 1 : 0)
                .ThenByDescending(h => h.WatchedAt)
                .Select(h => h.MovieTitle)
                .Distinct()
                .ToList();

            var recommendedIds = await _groq.RecommendMoviesAsync(watchedTitles, favoriteGenres, unwatched);

            List<MovieDTO> result;
            string message;

            if (recommendedIds.Count > 0)
            {
                var page = await _movies.GetMoviesAsync(new FilterMoviesDTO
                    { Ids = recommendedIds, PageSize = recommendedIds.Count });
                var map = page.Items.ToDictionary(m => m.Id);
                result  = recommendedIds.Where(map.ContainsKey).Select(id => map[id]).Take(9).ToList();
                message = "Gợi ý AI";
            }
            else
            {
                var allMovieDtos = await GetAllMovieDtosAsync();
                result = FallbackRecommend(
                    allMovieDtos.Where(m => !watchedTitleSet.Contains(m.Title)
                                            && !inProgressIds.Contains(m.Id)).ToList(),
                    favoriteGenres, take: 9);
                message = "Gợi ý phổ biến";
            }

            return Ok(ApiResponse.Ok(result, message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI] Recommend failed for userId={UserId}", userId);
            return StatusCode(503, ApiResponse.Fail("Không thể lấy gợi ý lúc này. Vui lòng thử lại."));
        }
    }

    // ─── GET /api/ai/recommend/tvshows ────────────────────────────────────────

    /// <summary>
    /// Gợi ý TV show dựa trên lịch sử xem series của user.
    /// Fallback về top-rated nếu AI không trả về kết quả.
    /// </summary>
    [HttpGet("recommend/tvshows")]
    [Authorize]
    public async Task<IActionResult> RecommendTvShows()
    {
        Guid userId;
        try
        {
            userId = GetUserId();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse.Fail(ex.Message));
        }

        try
        {
            var history  = (await _tvShows.GetWatchHistoryAsync(userId)).ToList();
            var contexts = await GetCachedTvShowContextsAsync();

            var watchedTitleSet = history
                .Select(h => h.TvShowTitle)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var watchedTitles = history
                .OrderByDescending(h => h.IsCompleted ? 1 : 0)
                .ThenByDescending(h => h.WatchedAt)
                .Select(h => h.TvShowTitle)
                .Distinct()
                .ToList();

            // Xây favoriteGenres từ lịch sử xem series
            var favoriteGenres = contexts
                .Where(s => watchedTitleSet.Contains(s.Title))
                .SelectMany(s => s.Genres.Split(',').Select(g => g.Trim()))
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key)
                .ToList();

            var unwatched = contexts
                .Where(s => !watchedTitleSet.Contains(s.Title))
                .ToList();

            var recommendedIds = await _groq.RecommendTvShowsAsync(
                watchedTitles, favoriteGenres, unwatched);

            List<TvShowSummaryDTO> result;
            string message;

            if (recommendedIds.Count > 0)
            {
                var page = await _tvShows.GetTvShowsAsync(
                    new FilterTvShowsDTO { Ids = recommendedIds, PageSize = recommendedIds.Count });
                var map = page.Items.ToDictionary(s => s.Id);
                result  = recommendedIds.Where(map.ContainsKey).Select(id => map[id]).Take(9).ToList();
                message = "Gợi ý AI";
            }
            else
            {
                var allDtos  = await GetAllTvShowDtosAsync();
                var genreSet = favoriteGenres.Select(g => g.ToLowerInvariant()).ToHashSet();
                result = allDtos
                    .Where(s => !watchedTitleSet.Contains(s.Title))
                    .OrderByDescending(s =>
                        s.Genres.Count(g => genreSet.Contains(g.ToLowerInvariant())) * 2.0
                        + (double)(s.Rating ?? 0))
                    .Take(9)
                    .ToList();
                message = "Gợi ý phổ biến";
            }

            return Ok(ApiResponse.Ok(result, message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI] RecommendTvShows failed for userId={UserId}", userId);
            return StatusCode(503, ApiResponse.Fail("Không thể lấy gợi ý lúc này. Vui lòng thử lại."));
        }
    }

    // ─── GET /api/ai/search?q=... ─────────────────────────────────────────────

    [HttpGet("search")]
    public async Task<IActionResult> SmartSearch([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(ApiResponse.Fail("Query không được để trống."));

        if (q.Length > MaxQueryLength)
            return BadRequest(ApiResponse.Fail($"Query không được vượt quá {MaxQueryLength} ký tự."));

        try
        {
            var basic    = (await _movies.SearchMoviesAsync(q)).ToList();
            var contexts = await GetCachedContextsAsync();

            if (basic.Count >= 5)
                return Ok(ApiResponse.Ok(basic, "Kết quả tìm kiếm"));

            var aiIds = await _groq.SmartSearchAsync(q, contexts);

            if (aiIds.Count == 0)
                return Ok(ApiResponse.Ok(basic,
                    basic.Count > 0 ? "Kết quả tìm kiếm" : "Không tìm thấy kết quả phù hợp."));

            var page  = await _movies.GetMoviesAsync(
                new FilterMoviesDTO { Ids = aiIds, PageSize = aiIds.Count });
            var map   = page.Items.ToDictionary(m => m.Id);
            var aiSet = aiIds.ToHashSet();

            var merged = aiIds
                .Where(map.ContainsKey)
                .Select(id => map[id])
                .Concat(basic.Where(m => !aiSet.Contains(m!.Id)))
                .ToList();

            return Ok(ApiResponse.Ok(merged, "Kết quả tìm kiếm AI"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI] SmartSearch failed for query={Query}", q);
            return StatusCode(503, ApiResponse.Fail("Không thể tìm kiếm lúc này. Vui lòng thử lại."));
        }
    }

    // ─── GET /api/ai/search/tvshows?q=... ────────────────────────────────────

    /// <summary>
    /// AI search TV show bằng ngôn ngữ tự nhiên.
    /// Luồng: basic search trước → nếu ít hơn 5 kết quả → gọi AI → merge.
    /// </summary>
    [HttpGet("search/tvshows")]
    public async Task<IActionResult> SmartSearchTvShows([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(ApiResponse.Fail("Query không được để trống."));

        if (q.Length > MaxQueryLength)
            return BadRequest(ApiResponse.Fail($"Query không được vượt quá {MaxQueryLength} ký tự."));

        try
        {
            var basic    = (await _tvShows.SearchTvShowsAsync(q)).ToList();
            var contexts = await GetCachedTvShowContextsAsync();

            if (basic.Count >= 5)
                return Ok(ApiResponse.Ok(basic, "Kết quả tìm kiếm"));

            var aiIds = await _groq.SmartSearchTvShowsAsync(q, contexts);

            if (aiIds.Count == 0)
                return Ok(ApiResponse.Ok(basic,
                    basic.Count > 0 ? "Kết quả tìm kiếm" : "Không tìm thấy kết quả phù hợp."));

            var page  = await _tvShows.GetTvShowsAsync(
                new FilterTvShowsDTO { Ids = aiIds, PageSize = aiIds.Count });
            var map   = page.Items.ToDictionary(s => s.Id);
            var aiSet = aiIds.ToHashSet();

            var merged = aiIds
                .Where(map.ContainsKey)
                .Select(id => map[id])
                .Concat(basic.Where(s => !aiSet.Contains(s.Id)))
                .ToList();

            return Ok(ApiResponse.Ok(merged, "Kết quả tìm kiếm AI"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI] SmartSearchTvShows failed for query={Query}", q);
            return StatusCode(503, ApiResponse.Fail("Không thể tìm kiếm lúc này. Vui lòng thử lại."));
        }
    }

    // ─── Chat Intent Handlers ─────────────────────────────────────────────────

    /// <summary>
    /// Handler cho intent "site" — trả lời câu hỏi về website bằng SiteKnowledge.
    /// Không cần movie context, không trả kèm movie cards.
    /// </summary>
    private async Task<IActionResult> HandleSiteChatAsync(AiChatRequestDTO dto)
    {
        var systemContext = MoviePrompts.SiteGuideSystem
                            + "\n\n"
                            + MoviePrompts.SiteKnowledge;

        var history = BuildCleanHistory(dto.History);
        var reply   = await _groq.ChatAsync(dto.Message, systemContext, history);

        return Ok(ApiResponse.Ok(new
        {
            reply,
            movies  = Array.Empty<object>(),
            tvshows = Array.Empty<object>(),
            intent  = "site",
        }));
    }

    /// <summary>
    /// [FIX-4] Handler cho intent "mood" — reply do AI sinh ra thay vì hardcode string.
    /// Extract mood keyword → gọi MoodRecommend logic → AI tạo reply tự nhiên + movie cards.
    /// </summary>
    private async Task<IActionResult> HandleMoodChatAsync(AiChatRequestDTO dto)
    {
        var detectedMood = DetectMoodKeyword(dto.Message);

        if (string.IsNullOrEmpty(detectedMood))
        {
            // Mood không rõ → fallback về movie chat để AI hỏi lại tự nhiên
            return await HandleMovieChatAsync(dto);
        }

        var moodResult = await GetMoodMoviesInternalAsync(detectedMood);

        // [FIX-4] Để AI sinh reply tự nhiên thay vì hardcode chuỗi
        var movieTitles = moodResult.Count > 0
            ? string.Join(", ", moodResult.Take(4).Select(m => m.Title))
            : string.Empty;

        var moodContext = moodResult.Count > 0
            ? $"Bạn vừa tìm được {moodResult.Count} phim phù hợp với tâm trạng \"{detectedMood}\": {movieTitles}."
            : $"Không có phim nào thật sự phù hợp với tâm trạng \"{detectedMood}\" trong danh sách hiện tại.";

        var systemContext = MoviePrompts.ChatSystem + "\n\n" + moodContext;
        var history       = BuildCleanHistory(dto.History);
        var reply         = await _groq.ChatAsync(dto.Message, systemContext, history);

        return Ok(ApiResponse.Ok(new
        {
            reply,
            movies  = moodResult,
            tvshows = Array.Empty<object>(),
            intent  = "mood",
        }));
    }

    /// <summary>
    /// Handler cho intent "compare" — extract 2 tên phim từ message → so sánh.
    /// [FIX-5] Dùng fuzzy match thay vì exact title match.
    /// Nếu không đủ thông tin → fallback về movie chat.
    /// </summary>
    private async Task<IActionResult> HandleCompareChatAsync(AiChatRequestDTO dto)
    {
        var contexts        = await GetCachedContextsAsync();
        var (movieA, movieB) = FindTwoMoviesInMessage(dto.Message, contexts);

        if (movieA is null || movieB is null)
        {
            // Không tìm ra 2 phim → xử lý như chat thường, AI sẽ hỏi lại
            return await HandleMovieChatAsync(dto);
        }

        var cacheKey = BuildCompareCacheKey(movieA.Id, movieB.Id);
        var cached   = await _cache.GetAsync<CompareResultDTO>(cacheKey);

        CompareResultDTO result;
        if (cached != null)
        {
            result = cached;
        }
        else
        {
            var movieADetail = await _movies.GetMovieByIdAsync(movieA.Id);
            var movieBDetail = await _movies.GetMovieByIdAsync(movieB.Id);

            if (movieADetail is null || movieBDetail is null)
                return await HandleMovieChatAsync(dto);

            var userPrompt = MoviePrompts.BuildCompareUser(
                titleA:    movieADetail.Title,
                genresA:   string.Join(", ", movieADetail.Genres),
                ratingA:   (double)(movieADetail.Rating ?? 0),
                directorA: movieADetail.Director ?? "Không rõ",
                yearA:     movieADetail.ReleaseDate?.Year,
                descA:     TruncateDescription(movieADetail.Description),
                titleB:    movieBDetail.Title,
                genresB:   string.Join(", ", movieBDetail.Genres),
                ratingB:   (double)(movieBDetail.Rating ?? 0),
                directorB: movieBDetail.Director ?? "Không rõ",
                yearB:     movieBDetail.ReleaseDate?.Year,
                descB:     TruncateDescription(movieBDetail.Description));

            var markdownTable = await _groq.ChatAsync(userPrompt, MoviePrompts.CompareSystem);

            result = new CompareResultDTO
            {
                MovieA        = movieADetail,
                MovieB        = movieBDetail,
                MarkdownTable = markdownTable,
            };

            await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(30));
        }

        return Ok(ApiResponse.Ok(new
        {
            reply        = $"Đây là bảng so sánh giữa **{result.MovieA.Title}** và **{result.MovieB.Title}**:",
            movies       = new[] { result.MovieA, result.MovieB },
            tvshows      = Array.Empty<object>(),
            intent       = "compare",
            compareTable = result.MarkdownTable,
        }));
    }

    /// <summary>
    /// Handler cho intent "review" — tóm tắt review phim được nhắc đến trong chat.
    /// Extract tên phim từ message → tìm phim → lấy review → tóm tắt.
    /// </summary>
    private async Task<IActionResult> HandleReviewChatAsync(AiChatRequestDTO dto)
    {
        var contexts = await GetCachedContextsAsync();
        var lower    = dto.Message.ToLowerInvariant();

        // [FIX-5] Dùng fuzzy match để tìm phim được nhắc đến
        var mentioned = contexts
            .Select(m => new { Movie = m, Score = FuzzyTitleScore(m.Title, lower) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault()?.Movie;

        if (mentioned is null)
            return await HandleMovieChatAsync(dto);

        var cacheKey = $"ai:review:{mentioned.Id}";
        var cached   = await _cache.GetAsync<ReviewSummaryResultDTO>(cacheKey);

        string summary;
        if (cached != null)
        {
            summary = cached.Summary;
        }
        else
        {
            var reviews = await _reviews.GetMovieReviewsAsync(mentioned.Id);
            var reviewTexts = reviews
                .Where(r => !string.IsNullOrWhiteSpace(r.ReviewText))
                .Select(r => r.ReviewText!)
                .ToList();

            if (reviewTexts.Count == 0)
            {
                summary = $"Phim \"{mentioned.Title}\" chưa có đánh giá nào.";
            }
            else
            {
                var userPrompt = MoviePrompts.BuildReviewUser(mentioned.Title, reviewTexts);
                summary = await _groq.ChatAsync(userPrompt, MoviePrompts.ReviewSystem);
                summary = string.IsNullOrWhiteSpace(summary)
                    ? "Không thể tóm tắt đánh giá lúc này."
                    : summary;

                var resultToCache = new ReviewSummaryResultDTO { MovieId = mentioned.Id, Summary = summary };
                await _cache.SetAsync(cacheKey, resultToCache, TimeSpan.FromMinutes(60));
            }
        }

        var history = BuildCleanHistory(dto.History);
        var reply   = await _groq.ChatAsync(
            $"Hãy trình bày tóm tắt đánh giá sau cho phim \"{mentioned.Title}\" một cách tự nhiên: {summary}",
            MoviePrompts.ChatSystem,
            history);

        return Ok(ApiResponse.Ok(new
        {
            reply,
            movies  = Array.Empty<object>(),
            tvshows = Array.Empty<object>(),
            intent  = "review",
        }));
    }

    /// <summary>
    /// Handler mặc định cho intent "movie" — giữ nguyên logic cũ + cast context.
    /// </summary>
    private async Task<IActionResult> HandleMovieChatAsync(AiChatRequestDTO dto)
    {
        var contexts  = await GetCachedContextsAsync();
        var topMovies = SelectMoviesForChat(contexts, dto.Message, dto.History, limit: 30);

        var movieLines = topMovies.Select(m =>
            $"- Phim: {m.Title} | Thể loại: {m.Genres} | Điểm: {m.Rating}/10" +
            (m.Year.HasValue ? $" | Năm: {m.Year}" : ""));

        // Detect xem user có đang hỏi về phim chiếu rạp / phim lẻ không.
        // Check cả message lẫn history gần nhất (3 turn) để giữ context qua nhiều lượt:
        // VD: turn 1 "phim chiếu rạp hay?" → turn 2 "gợi ý thêm đi" → vẫn nhận theaterNote.
        var theaterKeywords = new[] { "chiếu rạp", "phim lẻ", "phim điện ảnh", "cinema" };
        var recentUserText  = string.Join(" ", dto.History
                                  .Where(h => h.Role == "user")
                                  .TakeLast(3)
                                  .Select(h => h.Content))
                              + " " + dto.Message;
        var recentUserLower  = recentUserText.ToLowerInvariant();
        var isTheaterQuery   = theaterKeywords.Any(k => recentUserLower.Contains(k));
        var theaterNote = isTheaterQuery
            ? "\n\nLƯU Ý: User đang hỏi về PHIM LẺ/PHIM CHIẾU RẠP. Chỉ gợi ý phim lẻ (không phải series/phim bộ/hoạt hình dài tập)."
            : string.Empty;

        var dbContext = "Dưới đây là dữ liệu các phim đang có trên website của bạn:\n"
                        + string.Join("\n", movieLines)
                        + "\n\nQUY TẮC BẮT BUỘC: Nếu người dùng hỏi xin phim, tư vấn phim, CHỈ ĐƯỢC PHÉP lấy các phim trong danh sách trên để trả lời. TUYỆT ĐỐI KHÔNG tự bịa ra phim ở ngoài."
                        + theaterNote;

        var castContext = await BuildCastContextIfNeededAsync(dto.Message, dto.History, topMovies);
        if (!string.IsNullOrEmpty(castContext))
            dbContext += "\n\n" + castContext;

        var fullSystemContext = MoviePrompts.ChatSystem + "\n\n" + dbContext;
        var history           = BuildCleanHistory(dto.History);

        var reply              = await _groq.ChatAsync(dto.Message, fullSystemContext, history);
        var mentionedMovieDtos = await ExtractMentionedMoviesAsync(reply, topMovies);

        return Ok(ApiResponse.Ok(new
        {
            reply,
            movies  = mentionedMovieDtos,
            tvshows = Array.Empty<object>(),
            intent  = "movie",
        }));
    }

    /// <summary>
    /// Handler cho intent "tvshow" — chat về phim bộ/series.
    /// Logic tương tự HandleMovieChatAsync nhưng dùng TvShowContext + TvShowSummaryDTO.
    /// </summary>
    private async Task<IActionResult> HandleTvShowChatAsync(AiChatRequestDTO dto)
    {
        var contexts = await GetCachedTvShowContextsAsync();
        var topShows = SelectTvShowsForChat(contexts, dto.Message, dto.History, limit: 30);

        var showLines = topShows.Select(s =>
            $"- Series: {s.Title} | Thể loại: {s.Genres} | Điểm: {s.Rating}/10" +
            (s.NumberOfSeasons.HasValue ? $" | {s.NumberOfSeasons} mùa" : ""));

        var dbContext = "Dưới đây là danh sách phim bộ/series đang có trên website:\n"
                        + string.Join("\n", showLines)
                        + "\n\nQUY TẮC BẮT BUỘC: Chỉ gợi ý series có trong danh sách trên. TUYỆT ĐỐI KHÔNG tự bịa thêm series ở ngoài.";

        var fullSystemContext = MoviePrompts.ChatSystem + "\n\n" + dbContext;
        var history           = BuildCleanHistory(dto.History);

        var reply            = await _groq.ChatAsync(dto.Message, fullSystemContext, history);
        var mentionedTvShows = await ExtractMentionedTvShowsAsync(reply, topShows);

        return Ok(ApiResponse.Ok(new
        {
            reply,
            movies  = Array.Empty<object>(),
            tvshows = mentionedTvShows,
            intent  = "tvshow",
        }));
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    private static List<ChatMessageDTO> BuildCleanHistory(List<ChatMessageDTO> history)
        => history
            .Where(h => h.Role is "user" or "assistant"
                        && !string.IsNullOrWhiteSpace(h.Content))
            .TakeLast(MaxHistoryTurns)
            .Select(h => new ChatMessageDTO { Role = h.Role, Content = h.Content.Trim() })
            .ToList();

    /// <summary>
    /// Extract mood keyword từ message — map sang key trong MoodGenreMap.
    /// </summary>
    private static string DetectMoodKeyword(string message)
    {
        var lower = message.ToLowerInvariant();
        foreach (var mood in MoviePrompts.MoodGenreMap.Keys)
        {
            if (lower.Contains(mood))
                return mood;
        }

        return string.Empty;
    }

    /// <summary>
    /// [FIX-5] Fuzzy title score — cho phép match partial / bỏ stop words.
    /// VD: "dark knight" matches "The Dark Knight" với score cao.
    /// </summary>
    private static int FuzzyTitleScore(string title, string messageLower)
    {
        var titleLower = title.ToLowerInvariant();

        // Exact match — điểm cao nhất
        if (messageLower.Contains(titleLower))
            return 100;

        // Token match — mỗi word trong title xuất hiện trong message
        var titleTokens = titleLower
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2) // bỏ stop words ngắn: "the", "of", "a"
            .ToList();

        if (titleTokens.Count == 0) return 0;

        var matchedTokens = titleTokens.Count(t => messageLower.Contains(t));
        var ratio         = (double)matchedTokens / titleTokens.Count;

        // Cần ít nhất 60% token match để tránh false positive
        return ratio >= 0.6 ? (int)(ratio * 80) : 0;
    }

    /// <summary>
    /// [FIX-5] Tìm 2 phim được nhắc đến trong message — dùng fuzzy match.
    /// VD: "so sánh inception vs dark knight" → match "Inception" và "The Dark Knight".
    /// </summary>
    private static (MovieContext? A, MovieContext? B) FindTwoMoviesInMessage(
        string message,
        List<MovieContext> contexts)
    {
        var lower = message.ToLowerInvariant();

        var matches = contexts
            .Select(m => new { Movie = m, Score = FuzzyTitleScore(m.Title, lower) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(2)
            .ToList();

        return matches.Count >= 2 ? (matches[0].Movie, matches[1].Movie) : (null, null);
    }

    private static string BuildCompareCacheKey(Guid idA, Guid idB)
    {
        var minId = idA < idB ? idA : idB;
        var maxId = idA < idB ? idB : idA;
        return $"ai:compare:{minId}:{maxId}";
    }

    /// <summary>
    /// Shared internal logic cho mood recommend — dùng bởi cả MoodRecommend endpoint
    /// và HandleMoodChatAsync.
    /// </summary>
    private async Task<List<MovieDTO>> GetMoodMoviesInternalAsync(string normalizedMood)
    {
        var cacheKey = $"ai:mood:{normalizedMood}";
        var cached   = await _cache.GetAsync<MoodRecommendResultDTO>(cacheKey);
        if (cached != null) return cached.Movies;

        var contexts     = await GetCachedContextsAsync();
        var targetGenres = MoviePrompts.MoodGenreMap.TryGetValue(normalizedMood, out var genres)
            ? genres
            : [];

        var subset          = FilterMoviesForMood(contexts, targetGenres, limit: 20);
        var csv             = AiMovieCsvBuilder.Build(subset);
        var targetGenresStr = targetGenres.Length > 0
            ? string.Join(", ", targetGenres)
            : "any";

        List<MovieDTO> movies;
        var ids = await _groq.MoodRecommendAsync(normalizedMood, targetGenresStr, csv);

        if (ids.Count > 0)
        {
            var page = await _movies.GetMoviesAsync(
                new FilterMoviesDTO { Ids = ids, PageSize = ids.Count });
            var map = page.Items.ToDictionary(m => m.Id);
            movies = ids.Where(map.ContainsKey).Select(id => map[id]).ToList();
        }
        else
        {
            var allDtos = await GetAllMovieDtosAsync();
            movies = FallbackRecommend(allDtos, targetGenres.ToList(), take: 8);
        }

        if (movies.Count > 0)
        {
            var result = new MoodRecommendResultDTO { Mood = normalizedMood, Movies = movies };
            await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(15));
        }

        return movies;
    }

    private static List<MovieContext> FilterMoviesForMood(
        List<MovieContext> all,
        string[] targetGenres,
        int limit)
    {
        if (targetGenres.Length == 0)
            return all.OrderByDescending(m => m.Rating).Take(limit).ToList();

        var genreSet = targetGenres
            .Select(g => g.ToLowerInvariant())
            .ToHashSet();

        return all
            .OrderByDescending(m =>
            {
                var genreMatch = m.Genres.Split(',')
                    .Any(g => genreSet.Contains(g.Trim().ToLowerInvariant()))
                    ? 3
                    : 0;
                return genreMatch + m.Rating * 0.1;
            })
            .Take(limit)
            .ToList();
    }

    private async Task<(MovieDTO? A, MovieDTO? B)> FetchMoviePairAsync(Guid idA, Guid idB)
    {
        var tasks = new[] { _movies.GetMovieByIdAsync(idA), _movies.GetMovieByIdAsync(idB) };
        await Task.WhenAll(tasks);
        return (await tasks[0], await tasks[1]);
    }

    /// <summary>
    /// Parse reply text → tìm phim nào được mention → trả MovieDTO có posterUrl.
    /// KHÔNG gọi DB thêm — dùng GetAllMovieDtosAsync() đã cache sẵn (TTL 30 phút).
    /// </summary>
    private async Task<List<MovieDTO>> ExtractMentionedMoviesAsync(
        string reply,
        List<MovieContext> topMovies)
    {
        if (string.IsNullOrWhiteSpace(reply) || topMovies.Count == 0)
            return [];

        var replyLower = reply.ToLowerInvariant();

        var matchedIds = topMovies
            .Select(m => new
            {
                Id       = m.Id,
                Position = replyLower.IndexOf(m.Title.ToLowerInvariant(), StringComparison.Ordinal),
            })
            .Where(x => x.Position >= 0)
            .OrderBy(x => x.Position)
            .Take(MaxMovieCards)
            .Select(x => x.Id)
            .ToHashSet();

        if (matchedIds.Count == 0)
            return [];

        try
        {
            var allDtos = await GetAllMovieDtosAsync();
            return allDtos
                .Where(m => matchedIds.Contains(m.Id))
                .OrderBy(m =>
                {
                    var pos = replyLower.IndexOf(m.Title.ToLowerInvariant(), StringComparison.Ordinal);
                    return pos >= 0 ? pos : int.MaxValue;
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AI] ExtractMentionedMovies: không thể lấy MovieDTO từ cache");
            return [];
        }
    }

    /// <summary>
    /// Parse reply → tìm series được mention → trả TvShowSummaryDTO để frontend render cards.
    /// Dùng position-based ordering (mention đầu tiên = quan trọng nhất).
    /// Dùng GetAllTvShowDtosAsync() để lấy PosterUrl/BackdropUrl từ cache (không gọi DB thêm).
    /// </summary>
    private async Task<List<TvShowSummaryDTO>> ExtractMentionedTvShowsAsync(
        string reply,
        List<TvShowContext> topShows)
    {
        if (string.IsNullOrWhiteSpace(reply) || topShows.Count == 0)
            return [];

        var replyLower = reply.ToLowerInvariant();

        var matchedIds = topShows
            .Select(s => new
            {
                Id       = s.Id,
                Position = replyLower.IndexOf(s.Title.ToLowerInvariant(), StringComparison.Ordinal),
            })
            .Where(x => x.Position >= 0)
            .OrderBy(x => x.Position)
            .Take(MaxMovieCards)
            .ToDictionary(x => x.Id, x => x.Position);

        if (matchedIds.Count == 0)
            return [];

        try
        {
            var allDtos = await GetAllTvShowDtosAsync();
            return allDtos
                .Where(s => matchedIds.ContainsKey(s.Id))
                .OrderBy(s => matchedIds[s.Id])
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AI] ExtractMentionedTvShows: không thể lấy TvShowSummaryDTO từ cache");
            return [];
        }
    }

    /// <summary>
    /// Detect xem user có đang hỏi về diễn viên/đạo diễn không.
    /// Nếu có → tìm phim liên quan trong topMovies → fetch detail → build cast/director context.
    /// </summary>
    private async Task<string> BuildCastContextIfNeededAsync(
        string message,
        List<ChatMessageDTO> history,
        List<MovieContext> topMovies)
    {
        var fullText = string.Join(" ", history.TakeLast(2).Select(h => h.Content)) + " " + message;
        var lower    = fullText.ToLowerInvariant();

        var isCastQuery = CastKeywords.Any(k => lower.Contains(k));
        if (!isCastQuery) return string.Empty;

        var mentionedMovies = topMovies
            .Where(m => lower.Contains(m.Title.ToLowerInvariant()))
            .Take(3)
            .ToList();

        if (mentionedMovies.Count == 0)
            mentionedMovies = topMovies.Take(3).ToList();

        var detailTasks = mentionedMovies.Select(m => _movies.GetMovieByIdAsync(m.Id));

        MovieDTO?[] details;
        try
        {
            details = await Task.WhenAll(detailTasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AI] Không thể fetch movie detail cho cast context");
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("--- THÔNG TIN CHI TIẾT DIỄN VIÊN & ĐẠO DIỄN ---");

        foreach (var detail in details)
        {
            if (detail is null) continue;

            sb.AppendLine($"\nPhim: {detail.Title}");

            if (!string.IsNullOrEmpty(detail.Director))
                sb.AppendLine($"  Đạo diễn: {detail.Director}");

            if (detail.Cast is { Count: > 0 })
            {
                var castList = detail.Cast
                    .OrderBy(c => c.Order)
                    .Take(10)
                    .Select(c => string.IsNullOrEmpty(c.Character)
                        ? c.Name
                        : $"{c.Name} (vai {c.Character})");

                sb.AppendLine($"  Diễn viên: {string.Join(", ", castList)}");
            }
        }

        return sb.ToString();
    }

    // ── Movie chat selector ───────────────────────────────────────────────────

    // Map từ khóa câu hỏi thường gặp → genre ưu tiên để lọc phim lẻ
    // VD: "phim chiếu rạp hay" → ưu tiên Action, Thriller, Adventure (không phải anime)
    private static readonly Dictionary<string, string[]> ChatQueryGenreHints =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["chiếu rạp"]    = ["Action", "Thriller", "Adventure", "Drama", "Comedy"],
            ["phim hay"]     = ["Drama", "Action", "Adventure", "Thriller"],
            ["phim mới"]     = ["Action", "Drama", "Comedy", "Thriller"],
            ["phim hot"]     = ["Action", "Thriller", "Drama"],
            ["phim tình cảm"]= ["Romance", "Drama"],
            ["phim hành động"]= ["Action", "Adventure"],
            ["phim kinh dị"] = ["Horror", "Thriller"],
            ["phim hài"]     = ["Comedy"],
            ["phim khoa học"]= ["Science Fiction"],
            ["phim tâm lý"]  = ["Drama", "Thriller", "Mystery"],
            ["phim lịch sử"] = ["History", "Drama", "War"],
            ["phim fantasy"] = ["Fantasy", "Adventure"],
            ["phim animation"]= ["Animation"],
            // Phân biệt rõ: câu hỏi phim lẻ KHÔNG nên trả về anime/hoạt hình
            ["phim lẻ"]      = ["Action", "Drama", "Thriller", "Comedy", "Romance"],
            ["phim điện ảnh"]= ["Action", "Drama", "Thriller", "Comedy"],
        };

    private static List<MovieContext> SelectMoviesForChat(
        List<MovieContext> all,
        string currentMessage,
        List<ChatMessageDTO> history,
        int limit)
    {
        var recentText = string.Join(" ", history.TakeLast(3).Select(h => h.Content))
                         + " " + currentMessage;

        var tokens = recentText
            .ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', '?', '!', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .ToHashSet();

        if (tokens.Count == 0)
            return all.Take(limit).ToList();

        // Tính score dựa trên title/genre/description match
        var scored = all
            .Select(m => new
            {
                Movie = m,
                Score = CalcChatScore(m, tokens) + m.Rating * 0.05
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        // Kiểm tra xem có genre hint từ query không (VD: "chiếu rạp", "phim hay")
        // Áp dụng khi top movies có score thấp (không match rõ ràng) để tránh trả toàn anime
        var textLower = recentText.ToLowerInvariant();
        var hintGenres = ChatQueryGenreHints
            .Where(kv => textLower.Contains(kv.Key))
            .SelectMany(kv => kv.Value)
            .Select(g => g.ToLowerInvariant())
            .ToHashSet();

        // Nếu có hint genre → luôn re-rank để đảm bảo phim đúng thể loại lên đầu.
        // Điều kiện cũ dùng || khiến re-rank xảy ra ngay cả khi token score đã tốt;
        // đổi thành luôn re-rank khi hintGenres có giá trị — chi phí thấp, kết quả chính xác hơn.
        //
        // Thêm animePenalty: khi query rõ ràng là phim lẻ/chiếu rạp, trừ điểm phim Animation
        // để tránh Dragon Ball / anime dài tập leo lên đầu nhờ rating cao.
        if (hintGenres.Count > 0)
        {
            var isTheaterOrStandaloneQuery = textLower.Contains("chiếu rạp")
                                             || textLower.Contains("phim lẻ")
                                             || textLower.Contains("phim điện ảnh")
                                             || textLower.Contains("cinema");

            return all
                .Select(m => new
                {
                    Movie = m,
                    Score = CalcChatScore(m, tokens)
                            + (m.Genres.Split(',').Any(g => hintGenres.Contains(g.Trim().ToLowerInvariant()))
                                ? 5.0 : 0.0)   // genre hint boost
                            + (isTheaterOrStandaloneQuery
                               && m.Genres.Split(',').Any(g =>
                                   g.Trim().ToLowerInvariant() is "animation" or "anime")
                                ? -4.0 : 0.0)  // animation penalty cho query phim lẻ
                            + m.Rating * 0.05
                })
                .OrderByDescending(x => x.Score)
                .Take(limit)
                .Select(x => x.Movie)
                .ToList();
        }

        return scored.Take(limit).Select(x => x.Movie).ToList();
    }

    private static double CalcChatScore(MovieContext m, HashSet<string> tokens)
    {
        var titleLower  = m.Title.ToLowerInvariant();
        var genresLower = m.Genres.ToLowerInvariant();
        var descLower   = m.Description.ToLowerInvariant();

        return tokens.Count(t => titleLower.Contains(t))  * 3.0
             + tokens.Count(t => genresLower.Contains(t)) * 2.0
             + tokens.Count(t => descLower.Contains(t))   * 1.0;
    }

    // ── TV Show chat selector ─────────────────────────────────────────────────

    /// <summary>
    /// Chọn top N series phù hợp nhất với câu hỏi — tương tự SelectMoviesForChat.
    /// </summary>
    private static List<TvShowContext> SelectTvShowsForChat(
        List<TvShowContext> all,
        string currentMessage,
        List<ChatMessageDTO> history,
        int limit)
    {
        var recentText = string.Join(" ", history.TakeLast(3).Select(h => h.Content))
                         + " " + currentMessage;

        var tokens = recentText
            .ToLowerInvariant()
            .Split(new[] { ' ', ',', '.', '?', '!', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .ToHashSet();

        if (tokens.Count == 0)
            return all.Take(limit).ToList();

        return all
            .Select(s => new
            {
                Show  = s,
                Score = CalcTvShowChatScore(s, tokens) + s.Rating * 0.05
            })
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => x.Show)
            .ToList();
    }

    private static double CalcTvShowChatScore(TvShowContext s, HashSet<string> tokens)
    {
        var titleLower  = s.Title.ToLowerInvariant();
        var genresLower = s.Genres.ToLowerInvariant();
        var descLower   = s.Description.ToLowerInvariant();

        return tokens.Count(t => titleLower.Contains(t))  * 3.0
             + tokens.Count(t => genresLower.Contains(t)) * 2.0
             + tokens.Count(t => descLower.Contains(t))   * 1.0;
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static List<string> BuildFavoriteGenres(
        List<MovieContext> contexts, HashSet<Guid> watchedIds, bool fallbackToAll)
    {
        var genres = contexts
            .Where(m => watchedIds.Contains(m.Id))
            .SelectMany(m => m.Genres.Split(',').Select(g => g.Trim()))
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        if (genres.Count > 0 || !fallbackToAll) return genres;

        return contexts
            .SelectMany(m => m.Genres.Split(',').Select(g => g.Trim()))
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();
    }

    private static List<MovieDTO> FallbackRecommend(
        List<MovieDTO> movies, List<string> favoriteGenres, int take)
    {
        var genreSet = favoriteGenres.Select(g => g.ToLowerInvariant()).ToHashSet();
        return movies
            .OrderByDescending(m =>
                m.Genres.Count(g => genreSet.Contains(g.ToLowerInvariant())) * 2.0
                + (double)(m.Rating ?? 0))
            .Take(take)
            .ToList();
    }

    private static string TruncateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;
        if (description.Length <= DescriptionMaxLength) return description.Trim();
        var cut       = description[..DescriptionMaxLength];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > 0 ? cut[..lastSpace] : cut).Trim();
    }

    private async Task<(List<WatchHistoryDTO> history, List<MovieContext> contexts)>
        FetchDataAsync(Guid userId)
    {
        var history  = await _movies.GetWatchHistoryAsync(userId);
        var contexts = await GetCachedContextsAsync();
        return (history.ToList(), contexts);
    }

    // ── Movie cache helpers ───────────────────────────────────────────────────

    private async Task<List<MovieContext>> GetCachedContextsAsync()
    {
        var cached = await _cache.GetAsync<List<MovieContext>>(MovieContextsCacheKey);
        if (cached != null) return cached;

        await _contextCacheLock.WaitAsync();
        try
        {
            cached = await _cache.GetAsync<List<MovieContext>>(MovieContextsCacheKey);
            if (cached != null) return cached;

            var page = await _movies.GetMoviesAsync(new FilterMoviesDTO
            {
                PageSize = MovieFetchPageSize,
                SortBy   = "rating",
                SortDesc = true
            });

            var contexts = page.Items.Select(m => new MovieContext(
                Id:          m.Id,
                Title:       m.Title,
                Genres:      string.Join(", ", m.Genres),
                Rating:      (double)(m.Rating ?? 0),
                Year:        m.ReleaseDate?.Year,
                Description: TruncateDescription(m.Description)
            )).ToList();

            await _cache.SetAsync(MovieContextsCacheKey, contexts, ContextCacheTtl);
            return contexts;
        }
        finally
        {
            _contextCacheLock.Release();
        }
    }

    private async Task<List<MovieDTO>> GetAllMovieDtosAsync()
    {
        var cached = await _cache.GetAsync<List<MovieDTO>>(AllMovieDtosCacheKey);
        if (cached != null) return cached;

        await _dtoCacheLock.WaitAsync();
        try
        {
            cached = await _cache.GetAsync<List<MovieDTO>>(AllMovieDtosCacheKey);
            if (cached != null) return cached;

            var page = await _movies.GetMoviesAsync(new FilterMoviesDTO
            {
                PageSize = MovieFetchPageSize,
                SortBy   = "rating",
                SortDesc = true
            });

            var list = page.Items.ToList();
            await _cache.SetAsync(AllMovieDtosCacheKey, list, ContextCacheTtl);
            return list;
        }
        finally
        {
            _dtoCacheLock.Release();
        }
    }

    // ── TV Show cache helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Load và cache TvShowContext list — TTL 30 phút, double-checked locking.
    /// </summary>
    private async Task<List<TvShowContext>> GetCachedTvShowContextsAsync()
    {
        var cached = await _cache.GetAsync<List<TvShowContext>>(TvShowContextsCacheKey);
        if (cached != null) return cached;

        await _tvShowContextLock.WaitAsync();
        try
        {
            cached = await _cache.GetAsync<List<TvShowContext>>(TvShowContextsCacheKey);
            if (cached != null) return cached;

            var page = await _tvShows.GetTvShowsAsync(new FilterTvShowsDTO
            {
                PageSize = MovieFetchPageSize,
                SortBy   = "rating",
                SortDesc = true
            });

            var contexts = page.Items.Select(s => new TvShowContext(
                Id:              s.Id,
                Title:           s.Title,
                Genres:          string.Join(", ", s.Genres),
                Rating:          (double)(s.Rating ?? 0),
                Year:            s.FirstAirDate?.Year,
                Description:     TruncateDescription(s.Description),
                NumberOfSeasons: s.NumberOfSeasons
            )).ToList();

            await _cache.SetAsync(TvShowContextsCacheKey, contexts, ContextCacheTtl);
            return contexts;
        }
        finally
        {
            _tvShowContextLock.Release();
        }
    }

    /// <summary>
    /// Load và cache TvShowSummaryDTO list — TTL 30 phút, double-checked locking.
    /// </summary>
    private async Task<List<TvShowSummaryDTO>> GetAllTvShowDtosAsync()
    {
        var cached = await _cache.GetAsync<List<TvShowSummaryDTO>>(AllTvShowDtosCacheKey);
        if (cached != null) return cached;

        await _tvShowDtoCacheLock.WaitAsync();
        try
        {
            cached = await _cache.GetAsync<List<TvShowSummaryDTO>>(AllTvShowDtosCacheKey);
            if (cached != null) return cached;

            var page = await _tvShows.GetTvShowsAsync(new FilterTvShowsDTO
            {
                PageSize = MovieFetchPageSize,
                SortBy   = "rating",
                SortDesc = true
            });

            var list = page.Items.ToList();
            await _cache.SetAsync(AllTvShowDtosCacheKey, list, ContextCacheTtl);
            return list;
        }
        finally
        {
            _tvShowDtoCacheLock.Release();
        }
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    private Guid GetUserId()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(value) || !Guid.TryParse(value, out var id))
            throw new UnauthorizedAccessException("Token không hợp lệ hoặc thiếu claim UserId.");
        return id;
    }
}

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public record MoodRequestDTO
{
    public string Mood { get; init; } = string.Empty;
}

public record MoodRecommendResultDTO
{
    public string         Mood   { get; init; } = string.Empty;
    public List<MovieDTO> Movies { get; init; } = [];
}

public record CompareRequestDTO
{
    public Guid MovieIdA { get; init; }
    public Guid MovieIdB { get; init; }
}

public record CompareResultDTO
{
    public MovieDTO MovieA        { get; init; } = null!;
    public MovieDTO MovieB        { get; init; } = null!;
    public string   MarkdownTable { get; init; } = string.Empty;
}

public record ReviewRequestDTO
{
    public Guid MovieId { get; init; }
}

public record ReviewSummaryResultDTO
{
    public Guid   MovieId { get; init; }
    public string Summary { get; init; } = string.Empty;
}

// ─── Thin response wrapper ────────────────────────────────────────────────────
file static class ApiResponse
{
    internal static ApiResponseDTO<object> Ok(object data, string message = "Thành công")
        => new() { Data = data, Message = message, Success = true };

    internal static ApiResponseDTO<object> Fail(string message)
        => new() { Message = message, Success = false };
}
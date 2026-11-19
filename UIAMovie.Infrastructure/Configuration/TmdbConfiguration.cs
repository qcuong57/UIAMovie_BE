// UIAMovie.Infrastructure/Configuration/TmdbService.cs

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Infrastructure.Configuration;

public interface ITmdbService
{
    // ── Movie ─────────────────────────────────────────────────────────────────
    Task<TmdbMovieDetailDTO?> GetMovieAsync(int tmdbId);
    Task<TmdbSearchResponseDTO> SearchMoviesAsync(string query, int page = 1);
    Task<TmdbSearchResponseDTO> GetTrendingMoviesAsync(string timeWindow = "week");
    Task<List<TmdbTrailerDTO>> GetMovieTrailersAsync(int tmdbId);
    Task<List<TmdbGenreDTO>> GetGenresAsync();
    Task<TmdbCreditsResponseDTO?> GetCreditsAsync(int tmdbId);
    Task<TmdbImagesResponseDTO?> GetImagesAsync(int tmdbId);
    Task<TmdbPersonDetailDTO?> GetPersonDetailAsync(int tmdbPersonId);
    Task<List<string>> GetPersonImagesAsync(int tmdbPersonId);
    Task<TmdbFullMovieDTO?> GetFullMovieAsync(int tmdbId);

    // ── Discover (lọc + tìm kiếm kết hợp) ─────────────────────────────────────
    Task<TmdbDiscoverResponseDTO> DiscoverMoviesAsync(TmdbDiscoverFilterDTO filter);
    Task<TmdbDiscoverResponseDTO> DiscoverTvShowsAsync(TmdbDiscoverFilterDTO filter);

    // ── TV Show ───────────────────────────────────────────────────────────────
    Task<TmdbTvDetailDTO?> GetTvShowAsync(int tmdbId);
    Task<TmdbTvSearchResponseDTO> SearchTvShowsAsync(string query, int page = 1);
    Task<TmdbTvSearchResponseDTO> GetTrendingTvShowsAsync(string timeWindow = "week");
    Task<List<TmdbTrailerDTO>> GetTvTrailersAsync(int tmdbId);
    Task<TmdbTvCreditsResponseDTO?> GetTvCreditsAsync(int tmdbId);
    Task<TmdbImagesResponseDTO?> GetTvImagesAsync(int tmdbId);
    Task<List<TmdbGenreDTO>> GetTvGenresAsync();
    Task<TmdbSeasonDetailDTO?> GetTvSeasonDetailAsync(int tmdbId, int seasonNumber);
    Task<TmdbFullTvShowDTO?> GetFullTvShowAsync(int tmdbId);

    /// <summary>
    /// Bản nhẹ của GetFullTvShowAsync dành riêng cho "sync tập mới":
    /// chỉ lấy detail + season/episode (KHÔNG lấy cast, ảnh, trailer, person).
    /// </summary>
    Task<TmdbFullTvShowDTO?> GetTvShowForEpisodeSyncAsync(int tmdbId);
}

public class TmdbService : ITmdbService
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _translateClient; // tách riêng: không qua TmdbRetryHandler, không tự theo redirect
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TmdbService>? _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public TmdbService(IHttpClientFactory httpClientFactory, IConfiguration configuration,
        ILogger<TmdbService>? logger = null)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("tmdb"); // named client: có retry (xem TmdbRetryHandler)
        _translateClient = httpClientFactory.CreateClient("translate");
        _apiKey = configuration["TMDB:ApiKey"]!;
        _baseUrl = configuration["TMDB:BaseUrl"]!;
        _configuration = configuration;
    }

    // ════════════════════════════════════════════════════════════════════════
    // MOVIE METHODS — giữ nguyên, không thay đổi
    // ════════════════════════════════════════════════════════════════════════

    public async Task<TmdbMovieDetailDTO?> GetMovieAsync(int tmdbId)
    {
        var url = $"{_baseUrl}/movie/{tmdbId}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var movie = JsonSerializer.Deserialize<TmdbMovieDetailDTO>(content, _jsonOptions);

        if (movie != null)
        {
            // vi-VN không có overview → lấy en-US rồi dịch (TMDB không tự fallback overview theo language)
            (movie.Title, movie.Overview) = await LocalizeAsync("movie", tmdbId, movie.Title, movie.Overview);

            movie.PosterUrl = BuildImageUrl(movie.PosterPath);
            movie.BackdropUrl = BuildImageUrl(movie.BackdropPath, "original");
        }

        return movie;
    }

    public async Task<TmdbSearchResponseDTO> SearchMoviesAsync(string query, int page = 1)
    {
        var url =
            $"{_baseUrl}/search/movie?api_key={_apiKey}&query={Uri.EscapeDataString(query)}&page={page}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbSearchResponseDTO>(content, _jsonOptions)
                     ?? new TmdbSearchResponseDTO();

        foreach (var m in result.Results)
        {
            m.PosterUrl = BuildImageUrl(m.PosterPath);
            m.BackdropUrl = BuildImageUrl(m.BackdropPath, "original");
        }

        return result;
    }

    public async Task<TmdbSearchResponseDTO> GetTrendingMoviesAsync(string timeWindow = "week")
    {
        var url = $"{_baseUrl}/trending/movie/{timeWindow}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbSearchResponseDTO>(content, _jsonOptions)
                     ?? new TmdbSearchResponseDTO();

        foreach (var m in result.Results)
        {
            m.PosterUrl = BuildImageUrl(m.PosterPath);
            m.BackdropUrl = BuildImageUrl(m.BackdropPath, "original");
        }

        return result;
    }

    /// <summary>
    /// Lấy danh sách trailer với fallback 3 bước:
    /// 1. vi-VN  → ưu tiên trailer tiếng Việt (thuyết minh / lồng tiếng)
    /// 2. en-US  → fallback trailer tiếng Anh (đa số phim đều có)
    /// 3. (none) → lấy toàn bộ video không lọc ngôn ngữ, phòng trường hợp
    ///             TMDB lưu trailer nhưng không gắn đúng locale
    /// </summary>
    public async Task<List<TmdbTrailerDTO>> GetMovieTrailersAsync(int tmdbId)
    {
        var trailers = await FetchTrailersAsync($"{_baseUrl}/movie/{tmdbId}/videos", "vi-VN");
        if (trailers.Any()) return trailers;

        trailers = await FetchTrailersAsync($"{_baseUrl}/movie/{tmdbId}/videos", "en-US");
        if (trailers.Any()) return trailers;

        return await FetchTrailersAsync($"{_baseUrl}/movie/{tmdbId}/videos", null);
    }

    public async Task<List<TmdbGenreDTO>> GetGenresAsync()
    {
        var url = $"{_baseUrl}/genre/movie/list?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbGenreResponseDTO>(content, _jsonOptions);

        return result?.Genres ?? new();
    }

    public async Task<TmdbCreditsResponseDTO?> GetCreditsAsync(int tmdbId)
    {
        var url = $"{_baseUrl}/movie/{tmdbId}/credits?api_key={_apiKey}&language=en-US";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbCreditsResponseDTO>(content, _jsonOptions);

        if (result != null)
        {
            foreach (var c in result.Cast) c.ProfileUrl = BuildImageUrl(c.ProfilePath);
            foreach (var c in result.Crew) c.ProfileUrl = BuildImageUrl(c.ProfilePath);
        }

        return result;
    }

    public async Task<TmdbImagesResponseDTO?> GetImagesAsync(int tmdbId)
    {
        var url = $"{_baseUrl}/movie/{tmdbId}/images?api_key={_apiKey}";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbImagesResponseDTO>(content, _jsonOptions);

        if (result != null)
        {
            foreach (var img in result.Backdrops) img.Url = BuildImageUrl(img.FilePath, "original");
            foreach (var img in result.Posters) img.Url = BuildImageUrl(img.FilePath, "w500");
        }

        return result;
    }

    /// <summary>
    /// 1. Lấy bio tiếng Việt từ TMDB.
    /// 2. Nếu trống → lấy bio tiếng Anh.
    /// 3. Nếu vẫn tiếng Anh → tự động dịch sang tiếng Việt theo provider cấu hình.
    /// </summary>
    public async Task<TmdbPersonDetailDTO?> GetPersonDetailAsync(int tmdbPersonId)
    {
        var url = $"{_baseUrl}/person/{tmdbPersonId}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbPersonDetailDTO>(content, _jsonOptions);
        if (result == null) return null;

        if (string.IsNullOrWhiteSpace(result.Biography))
        {
            var enUrl = $"{_baseUrl}/person/{tmdbPersonId}?api_key={_apiKey}&language=en-US";
            var enResponse = await _httpClient.GetAsync(enUrl);

            if (enResponse.IsSuccessStatusCode)
            {
                var enContent = await enResponse.Content.ReadAsStringAsync();
                var enResult = JsonSerializer.Deserialize<TmdbPersonDetailDTO>(enContent, _jsonOptions);

                if (enResult != null && !string.IsNullOrWhiteSpace(enResult.Biography))
                    result.Biography = await TranslateTextAsync(enResult.Biography);
            }
        }

        result.ProfileUrl = BuildImageUrl(result.ProfilePath);
        return result;
    }

    public async Task<List<string>> GetPersonImagesAsync(int tmdbPersonId)
    {
        var url = $"{_baseUrl}/person/{tmdbPersonId}/images?api_key={_apiKey}";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return new();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbPersonImagesResponseDTO>(content, _jsonOptions);

        return result?.Profiles
            .OrderByDescending(p => p.VoteAverage)
            .Take(5)
            .Select(p => BuildImageUrl(p.FilePath, "w500"))
            .Where(u => !string.IsNullOrEmpty(u))
            .ToList() ?? new();
    }

    public async Task<TmdbFullMovieDTO?> GetFullMovieAsync(int tmdbId)
    {
        var detailTask = GetMovieAsync(tmdbId);
        var creditsTask = GetCreditsAsync(tmdbId);
        var imagesTask = GetImagesAsync(tmdbId);
        var trailersTask = GetMovieTrailersAsync(tmdbId);

        await Task.WhenAll(detailTask, creditsTask, imagesTask, trailersTask);

        var detail = detailTask.Result;
        if (detail == null) return null;

        var credits = creditsTask.Result;
        var images = imagesTask.Result;
        var trailers = trailersTask.Result;

        var top10Cast = credits?.Cast.OrderBy(c => c.Order).Take(10).ToList() ?? new();
        var director = credits?.Crew.FirstOrDefault(c => c.Job == "Director");

        var personIds = top10Cast.Select(c => c.Id).ToList();
        if (director != null) personIds.Add(director.Id);
        var distinctPersonIds = personIds.Distinct().ToList();

        var personDetailTasks = distinctPersonIds.ToDictionary(id => id, id => GetPersonDetailAsync(id));
        var personImageTasks = distinctPersonIds.ToDictionary(id => id, id => GetPersonImagesAsync(id));

        await Task.WhenAll(personDetailTasks.Values.Concat<Task>(personImageTasks.Values));

        return new TmdbFullMovieDTO
        {
            Detail = detail,
            Cast = top10Cast,
            Director = director,
            Backdrops = images?.Backdrops.OrderByDescending(i => i.VoteAverage).Take(5).ToList() ?? new(),
            Posters = images?.Posters.OrderByDescending(i => i.VoteAverage).Take(3).ToList() ?? new(),
            Trailers = trailers,
            PersonDetails = personDetailTasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result),
            PersonImages = personImageTasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result)
        };
    }

    // ════════════════════════════════════════════════════════════════════════
    // TV SHOW METHODS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lấy chi tiết 1 TV show từ TMDB.
    /// Response bao gồm season summary list — không cần gọi thêm endpoint.
    /// Để lấy episode list từng season, dùng GetTvSeasonDetailAsync().
    /// </summary>
    public async Task<TmdbTvDetailDTO?> GetTvShowAsync(int tmdbId)
    {
        var url = $"{_baseUrl}/tv/{tmdbId}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var show = JsonSerializer.Deserialize<TmdbTvDetailDTO>(content, _jsonOptions);
        if (show == null) return null;

        (show.Name, show.Overview) = await LocalizeAsync("tv", tmdbId, show.Name, show.Overview);

        show.PosterUrl = BuildImageUrl(show.PosterPath);
        show.BackdropUrl = BuildImageUrl(show.BackdropPath, "original");

        foreach (var s in show.Seasons)
            s.PosterUrl = BuildImageUrl(s.PosterPath);

        return show;
    }

    /// <summary>
    /// Tìm kiếm TV show trên TMDB — gọi /search/tv.
    /// </summary>
    public async Task<TmdbTvSearchResponseDTO> SearchTvShowsAsync(string query, int page = 1)
    {
        var url =
            $"{_baseUrl}/search/tv?api_key={_apiKey}&query={Uri.EscapeDataString(query)}&page={page}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbTvSearchResponseDTO>(content, _jsonOptions)
                     ?? new TmdbTvSearchResponseDTO();

        foreach (var tv in result.Results)
            NormalizeTvItem(tv);

        return result;
    }

    /// <summary>
    /// Lấy trending TV show — gọi /trending/tv/{timeWindow}.
    /// timeWindow: "day" hoặc "week" (mặc định "week").
    /// </summary>
    public async Task<TmdbTvSearchResponseDTO> GetTrendingTvShowsAsync(string timeWindow = "week")
    {
        var url = $"{_baseUrl}/trending/tv/{timeWindow}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbTvSearchResponseDTO>(content, _jsonOptions)
                     ?? new TmdbTvSearchResponseDTO();

        foreach (var tv in result.Results)
            NormalizeTvItem(tv);

        return result;
    }

    /// <summary>
    /// Lấy trailer TV show — fallback 3 bước giống movie.
    /// </summary>
    public async Task<List<TmdbTrailerDTO>> GetTvTrailersAsync(int tmdbId)
    {
        var baseVideoUrl = $"{_baseUrl}/tv/{tmdbId}/videos";

        var trailers = await FetchTrailersAsync(baseVideoUrl, "vi-VN");
        if (trailers.Any()) return trailers;

        trailers = await FetchTrailersAsync(baseVideoUrl, "en-US");
        if (trailers.Any()) return trailers;

        return await FetchTrailersAsync(baseVideoUrl, null);
    }

    /// <summary>
    /// Lấy cast + crew của TV show — gọi /tv/{id}/aggregate_credits.
    /// </summary>
    public async Task<TmdbTvCreditsResponseDTO?> GetTvCreditsAsync(int tmdbId)
    {
        var url = $"{_baseUrl}/tv/{tmdbId}/aggregate_credits?api_key={_apiKey}&language=en-US";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbTvCreditsResponseDTO>(content, _jsonOptions);

        if (result != null)
        {
            foreach (var c in result.Cast) c.ProfileUrl = BuildImageUrl(c.ProfilePath);
            foreach (var c in result.Crew) c.ProfileUrl = BuildImageUrl(c.ProfilePath);
        }

        return result;
    }

    /// <summary>
    /// Lấy ảnh backdrop + poster của TV show.
    /// </summary>
    public async Task<TmdbImagesResponseDTO?> GetTvImagesAsync(int tmdbId)
    {
        var url = $"{_baseUrl}/tv/{tmdbId}/images?api_key={_apiKey}";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbImagesResponseDTO>(content, _jsonOptions);

        if (result != null)
        {
            foreach (var img in result.Backdrops) img.Url = BuildImageUrl(img.FilePath, "original");
            foreach (var img in result.Posters) img.Url = BuildImageUrl(img.FilePath, "w500");
        }

        return result;
    }

    /// <summary>
    /// Lấy danh sách genre dành riêng cho TV.
    /// </summary>
    public async Task<List<TmdbGenreDTO>> GetTvGenresAsync()
    {
        var url = $"{_baseUrl}/genre/tv/list?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbGenreResponseDTO>(content, _jsonOptions);

        return result?.Genres ?? new();
    }

    /// <summary>
    /// Lấy chi tiết 1 season kèm episode list — có fallback + dịch overview giống biography.
    ///
    /// Luồng xử lý cho mỗi episode:
    ///   1. Lấy vi-VN → nếu overview có tiếng Việt thì dùng luôn.
    ///   2. Nếu overview trống → lấy en-US.
    ///   3. Nếu en-US có nội dung → dịch sang tiếng Việt qua TranslateTextAsync.
    ///
    /// Season overview cũng được xử lý tương tự.
    /// </summary>
    public Task<TmdbSeasonDetailDTO?> GetTvSeasonDetailAsync(int tmdbId, int seasonNumber)
        => GetTvSeasonDetailCoreAsync(tmdbId, seasonNumber, strict: false);

    /// <summary>
    /// strict = true: mọi lỗi HTTP trừ 404 (429/5xx đã hết retry, 401 sai API key...) ném HttpRequestException
    /// thay vì trả null. Dùng cho sync để không âm thầm bỏ qua season rồi báo "thành công, 0 tập mới".
    /// </summary>
    private async Task<TmdbSeasonDetailDTO?> GetTvSeasonDetailCoreAsync(int tmdbId, int seasonNumber, bool strict)
    {
        // ── Bước 1: Lấy bản vi-VN ────────────────────────────────────────────
        var url = $"{_baseUrl}/tv/{tmdbId}/season/{seasonNumber}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        if (strict && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbSeasonDetailDTO>(content, _jsonOptions);
        if (result == null) return null;

        result.PosterUrl = BuildImageUrl(result.PosterPath);
        foreach (var ep in result.Episodes)
            ep.StillUrl = BuildImageUrl(ep.StillPath, "w300");

        // ── Bước 2: Lấy bản en-US để fallback ────────────────────────────────
        // Chỉ gọi nếu season overview trống HOẶC có bất kỳ episode nào trống overview
        var needsEnFallback = string.IsNullOrWhiteSpace(result.Overview)
                              || result.Episodes.Any(ep => string.IsNullOrWhiteSpace(ep.Overview));

        TmdbSeasonDetailDTO? enResult = null;
        if (needsEnFallback)
        {
            var enUrl = $"{_baseUrl}/tv/{tmdbId}/season/{seasonNumber}?api_key={_apiKey}&language=en-US";
            var enResponse = await _httpClient.GetAsync(enUrl);
            if (enResponse.IsSuccessStatusCode)
            {
                var enContent = await enResponse.Content.ReadAsStringAsync();
                enResult = JsonSerializer.Deserialize<TmdbSeasonDetailDTO>(enContent, _jsonOptions);
            }
        }

        // ── Bước 3: Dịch season overview nếu cần ─────────────────────────────
        if (string.IsNullOrWhiteSpace(result.Overview)
            && enResult != null
            && !string.IsNullOrWhiteSpace(enResult.Overview))
        {
            result.Overview = await TranslateTextAsync(enResult.Overview);
        }

        // ── Bước 4: Dịch từng episode overview nếu cần ───────────────────────
        // Build lookup từ enResult để tránh nested loop
        var enEpisodeMap = enResult?.Episodes
                               .ToDictionary(e => e.EpisodeNumber, e => e.Overview)
                           ?? new Dictionary<int, string?>();

        // Gom các episode cần dịch rồi dịch theo lô (Groq: nhiều tập / 1 request; provider khác: song song)
        var pending = new List<(TmdbEpisodeDTO Ep, string En)>();
        foreach (var ep in result.Episodes)
        {
            if (!string.IsNullOrWhiteSpace(ep.Overview)) continue; // đã có tiếng Việt

            enEpisodeMap.TryGetValue(ep.EpisodeNumber, out var enOverview);
            if (string.IsNullOrWhiteSpace(enOverview)) continue; // en cũng trống

            pending.Add((ep, enOverview));
        }

        if (pending.Count > 0)
        {
            var translated = await TranslateManyAsync(pending.Select(x => x.En).ToList());
            for (var i = 0; i < pending.Count; i++)
                pending[i].Ep.Overview = translated[i];
        }

        return result;
    }

    /// <summary>
    /// Lấy toàn bộ dữ liệu cần thiết để import 1 TV show — gọi song song tất cả endpoints.
    ///
    /// Thứ tự:
    ///   Bước 1 — song song: detail, credits, images, trailers
    ///   Bước 2 — song song: season detail cho tất cả season > 0
    ///   Bước 3 — song song: person detail + person images cho top 10 cast + director
    ///
    /// Season 0 (Specials) bị bỏ qua hoàn toàn.
    /// </summary>
    public async Task<TmdbFullTvShowDTO?> GetFullTvShowAsync(int tmdbId)
    {
        // ── Bước 1: Gọi song song 4 TV endpoints ────────────────────────────
        var detailTask = GetTvShowAsync(tmdbId);
        var creditsTask = GetTvCreditsAsync(tmdbId);
        var imagesTask = GetTvImagesAsync(tmdbId);
        var trailersTask = GetTvTrailersAsync(tmdbId);

        await Task.WhenAll(detailTask, creditsTask, imagesTask, trailersTask);

        var detail = detailTask.Result;
        if (detail == null) return null;

        var credits = creditsTask.Result;
        var images = imagesTask.Result;
        var trailers = trailersTask.Result;

        // ── Bước 2: Gọi song song season detail (bỏ season 0) ───────────────
        var seasonNumbers = detail.Seasons
            .Where(s => s.SeasonNumber > 0)
            .Select(s => s.SeasonNumber)
            .ToList();

        var seasonTasks = seasonNumbers.ToDictionary(
            n => n,
            n => GetTvSeasonDetailAsync(tmdbId, n));

        await Task.WhenAll(seasonTasks.Values);

        var seasonDetails = seasonTasks
            .Where(kv => kv.Value.Result != null)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Result!);

        // ── Bước 3: Person detail + images (top 10 cast + director) ─────────
        var top10Cast = credits?.Cast
            .OrderBy(c => c.Order)
            .Take(10)
            .ToList() ?? new();

        var director = credits?.Crew.FirstOrDefault(c => c.Job == "Director")
                       ?? credits?.Crew.FirstOrDefault(c => c.Job == "Executive Producer");

        var personIds = top10Cast.Select(c => c.Id).ToList();
        if (director != null) personIds.Add(director.Id);
        var distinctPersonIds = personIds.Distinct().ToList();

        var personDetailTasks = distinctPersonIds.ToDictionary(id => id, id => GetPersonDetailAsync(id));
        var personImageTasks = distinctPersonIds.ToDictionary(id => id, id => GetPersonImagesAsync(id));

        await Task.WhenAll(personDetailTasks.Values.Concat<Task>(personImageTasks.Values));

        return new TmdbFullTvShowDTO
        {
            Detail = detail,
            Cast = top10Cast,
            Director = director,
            Backdrops = images?.Backdrops.OrderByDescending(i => i.VoteAverage).Take(5).ToList() ?? new(),
            Posters = images?.Posters.OrderByDescending(i => i.VoteAverage).Take(3).ToList() ?? new(),
            Trailers = trailers,
            SeasonDetails = seasonDetails,
            PersonDetails = personDetailTasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result),
            PersonImages = personImageTasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result)
        };
    }

    /// <summary>
    /// Chỉ lấy dữ liệu cần cho SyncNewEpisodesAsync: detail + season detail (kèm episodes).
    /// Giới hạn 3 season song song để không mở hàng chục kết nối TLS cùng lúc tới TMDB.
    /// Nếu 1 season lỗi mạng thì cả hàm ném exception (controller báo lỗi rõ ràng),
    /// tránh sync "nửa vời" rồi cập nhật NumberOfEpisodes/Status sai.
    /// </summary>
    public async Task<TmdbFullTvShowDTO?> GetTvShowForEpisodeSyncAsync(int tmdbId)
    {
        var detail = await GetTvShowAsync(tmdbId);
        if (detail == null) return null;

        using var gate = new SemaphoreSlim(3);

        var tasks = detail.Seasons
            .Where(s => s.SeasonNumber > 0)
            .Select(async s =>
            {
                await gate.WaitAsync();
                try
                {
                    var season = await GetTvSeasonDetailCoreAsync(tmdbId, s.SeasonNumber, strict: true);
                    return (s.SeasonNumber, Season: season);
                }
                finally
                {
                    gate.Release();
                }
            })
            .ToList();

        var results = await Task.WhenAll(tasks);

        return new TmdbFullTvShowDTO
        {
            Detail = detail,
            SeasonDetails = results
                .Where(r => r.Season != null)
                .ToDictionary(r => r.SeasonNumber, r => r.Season!)
        };
    }

    // ════════════════════════════════════════════════════════════════════════
    // SHARED PRIVATE HELPERS
    // ════════════════════════════════════════════════════════════════════════

    private async Task<List<TmdbTrailerDTO>> FetchTrailersAsync(string baseVideoUrl, string? language)
    {
        var langParam = language != null ? $"&language={language}" : string.Empty;
        var url = $"{baseVideoUrl}?api_key={_apiKey}{langParam}";
        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode) return new();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbVideoResponseDTO>(content, _jsonOptions);

        return result?.Results
            .Where(v => v.Type == "Trailer")
            .Select(v => new TmdbTrailerDTO
            {
                Key = v.Key,
                Name = v.Name,
                Type = v.Type,
                YoutubeUrl = $"https://www.youtube.com/watch?v={v.Key}"
            })
            .ToList() ?? new();
    }

    // Tên gốc viết bằng chữ Hàn/Nhật/Trung/Thái/Nga/Ả Rập (TMDB trả nguyên bản khi chưa có tên tiếng Việt)
    private static readonly Regex NonLatinTitle = new(
        @"[\u0400-\u04FF\u0600-\u06FF\u0E00-\u0E7F\u1100-\u11FF\u3040-\u30FF\u3400-\u9FFF\uAC00-\uD7AF]",
        RegexOptions.Compiled);

    /// <summary>
    /// Chuẩn hoá tên + overview cho import. kind = "movie" | "tv".
    /// Luôn lấy bản en-US từ TMDB (bất kể ngôn ngữ gốc là gì) rồi dịch sang tiếng Việt:
    ///  • Overview: en-US → TranslateTextAsync. en-US trống thì giữ bản vi-VN.
    ///  • Tên: nếu vi-VN đã là tên tiếng Việt thật (chữ Latin và khác tên en-US) thì giữ nguyên;
    ///    còn lại dịch tên en-US sang tiếng Việt (tắt bằng Translation:TranslateTitles = false → dùng tên en-US).
    /// </summary>
    private async Task<(string Title, string Overview)> LocalizeAsync(
        string kind, int tmdbId, string? viTitle, string? viOverview)
    {
        var title = viTitle ?? string.Empty;
        var overview = viOverview ?? string.Empty;

        string? enTitle = null, enOverview = null;
        try
        {
            var enResponse = await _httpClient.GetAsync(
                $"{_baseUrl}/{kind}/{tmdbId}?api_key={_apiKey}&language=en-US");
            if (!enResponse.IsSuccessStatusCode) return (title, overview);

            using var doc = JsonDocument.Parse(await enResponse.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.TryGetProperty(kind == "tv" ? "name" : "title", out var t)) enTitle = t.GetString();
            if (root.TryGetProperty("overview", out var o)) enOverview = o.GetString();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Không lấy được bản en-US của {Kind} {Id}", kind, tmdbId);
            return (title, overview);
        }

        // Overview: tiếng Anh → tiếng Việt
        if (!string.IsNullOrWhiteSpace(enOverview))
            overview = await TranslateTextAsync(enOverview);

        // Tên
        if (!string.IsNullOrWhiteSpace(enTitle) && !NonLatinTitle.IsMatch(enTitle))
        {
            var viIsRealVietnamese =
                !string.IsNullOrWhiteSpace(title)
                && !NonLatinTitle.IsMatch(title)
                && !string.Equals(title.Trim(), enTitle.Trim(), StringComparison.OrdinalIgnoreCase);

            if (!viIsRealVietnamese)
            {
                var translateTitles = !string.Equals(_configuration["Translation:TranslateTitles"], "false", StringComparison.OrdinalIgnoreCase);
                title = translateTitles ? await TranslateTextAsync(enTitle) : enTitle;
            }
        }

        return (title, overview);
    }

    private void NormalizeTvItem(TmdbTvDTO tv)
    {
        tv.PosterUrl = BuildImageUrl(tv.PosterPath);
        tv.BackdropUrl = BuildImageUrl(tv.BackdropPath, "original");
    }

    // ════════════════════════════════════════════════════════════════════════
    // DISCOVER — lọc + tìm kiếm kết hợp (phim & TV show)
    // ════════════════════════════════════════════════════════════════════════
    //  • Không có từ khoá → /discover/{movie|tv}: TMDB lọc chính xác (thể loại, quốc gia, năm, điểm, sắp xếp).
    //  • Có từ khoá       → /search/{movie|tv} rồi lọc tiếp ở server (endpoint search của TMDB không có bộ lọc).
    //    Phân trang theo "con trỏ": mỗi lần quét tối đa DiscoverMaxScanPages trang TMDB cho tới khi đủ
    //    DiscoverTargetCount kết quả; NextPage là trang TMDB cần quét tiếp.

    private const int DiscoverMaxScanPages = 5;
    private const int DiscoverTargetCount = 20;
    private const int TmdbMaxPage = 500;

    // Danh sách phim của TMDB không trả origin_country → khi tìm theo tên phải lọc gần đúng theo ngôn ngữ gốc
    private static readonly Dictionary<string, string[]> CountryLanguages =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["KR"] = new[] { "ko" },
            ["JP"] = new[] { "ja" },
            ["CN"] = new[] { "zh", "cn" },
            ["HK"] = new[] { "zh", "cn" },
            ["TW"] = new[] { "zh", "cn" },
            ["VN"] = new[] { "vi" },
            ["TH"] = new[] { "th" },
            ["IN"] = new[] { "hi", "ta", "te", "ml", "kn", "bn", "mr", "pa" },
            ["FR"] = new[] { "fr" },
            ["DE"] = new[] { "de" },
            ["ES"] = new[] { "es" },
            ["IT"] = new[] { "it" },
            ["US"] = new[] { "en" },
            ["GB"] = new[] { "en" },
            ["AU"] = new[] { "en" },
            ["CA"] = new[] { "en", "fr" },
        };

    private sealed record DiscoverCriteria(
        string? Query,
        List<int> Genres,
        string? Country,
        int? YearFrom,
        int? YearTo,
        double? MinRating,
        int? MinVotes,
        string Sort,
        int Page);

    public Task<TmdbDiscoverResponseDTO> DiscoverMoviesAsync(TmdbDiscoverFilterDTO filter)
        => DiscoverCoreAsync(false, filter);

    public Task<TmdbDiscoverResponseDTO> DiscoverTvShowsAsync(TmdbDiscoverFilterDTO filter)
        => DiscoverCoreAsync(true, filter);

    private static DiscoverCriteria NormalizeCriteria(TmdbDiscoverFilterDTO f)
    {
        var genres = (f.Genres ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .Take(10)
            .ToList();

        var country = f.Country?.Trim().ToUpperInvariant();
        if (country is null || country.Length != 2 || !country.All(char.IsLetter)) country = null;

        var maxYear = DateTime.UtcNow.Year + 5;
        int? ClampYear(int? y) => y.HasValue ? Math.Clamp(y.Value, 1888, maxYear) : null;
        var yearFrom = ClampYear(f.YearFrom);
        var yearTo = ClampYear(f.YearTo);
        if (yearFrom > yearTo) (yearFrom, yearTo) = (yearTo, yearFrom);

        double? minRating = f.MinRating is > 0 ? (double)Math.Min(f.MinRating.Value, 10m) : null;
        int? minVotes = f.MinVotes is > 0 ? f.MinVotes : null;

        var sort = (f.Sort ?? string.Empty).Trim().ToLowerInvariant();
        if (sort is not ("rating" or "newest" or "oldest")) sort = "popular";

        var query = string.IsNullOrWhiteSpace(f.Query) ? null : f.Query.Trim();

        return new DiscoverCriteria(
            query, genres, country, yearFrom, yearTo, minRating, minVotes, sort,
            Math.Clamp(f.Page, 1, TmdbMaxPage));
    }

    private async Task<TmdbDiscoverResponseDTO> DiscoverCoreAsync(bool isTv, TmdbDiscoverFilterDTO filter)
    {
        var c = NormalizeCriteria(filter ?? new TmdbDiscoverFilterDTO());
        var notes = new List<string>();

        // ── Không có từ khoá: TMDB tự lọc, phân trang chuẩn ──
        if (c.Query is null)
        {
            var (items, totalPages, totalResults) = await FetchDiscoverPageAsync(isTv, c, c.Page);

            // TMDB đã lọc theo with_origin_country nên điền luôn nước cho item chưa có (danh sách phim không trả field này)
            if (c.Country != null)
                foreach (var it in items.Where(i => i.OriginCountry.Count == 0))
                    it.OriginCountry = new List<string> { c.Country };

            var lastPage = Math.Min(totalPages, TmdbMaxPage);
            return new TmdbDiscoverResponseDTO
            {
                Results = items,
                Page = c.Page,
                NextPage = c.Page < lastPage ? c.Page + 1 : null,
                TotalPages = totalPages,
                TotalResults = totalResults,
                IsFiltered = false,
                Notes = notes
            };
        }

        // ── Có từ khoá: search rồi lọc tiếp ──
        string[]? languages = null;
        if (c.Country != null && !isTv)
        {
            if (CountryLanguages.TryGetValue(c.Country, out var langs))
            {
                languages = langs;
                notes.Add("Khi tìm theo tên, quốc gia được lọc gần đúng theo ngôn ngữ gốc của phim.");
            }
            else
            {
                notes.Add($"Không lọc được quốc gia {c.Country} khi tìm theo tên — bỏ từ khoá để lọc chính xác.");
            }
        }

        if (c.Sort != "popular")
            notes.Add("Khi tìm theo tên, kết quả được xếp theo độ liên quan.");

        var exactYear = c.YearFrom.HasValue && c.YearFrom == c.YearTo; // đã truyền thẳng cho TMDB
        var needsFilter =
            c.Genres.Count > 0
            || c.MinRating.HasValue
            || c.MinVotes.HasValue
            || ((c.YearFrom.HasValue || c.YearTo.HasValue) && !exactYear)
            || (c.Country != null && (isTv || languages != null));

        var results = new List<TmdbDiscoverItemDTO>();
        var seen = new HashSet<int>();
        var page = c.Page;
        var total = 1;
        var totalHits = 0;
        var scanned = 0;

        while (scanned < DiscoverMaxScanPages)
        {
            var (items, totalPages, totalResults) = await FetchSearchPageAsync(isTv, c, page);
            total = totalPages;
            totalHits = totalResults;
            scanned++;

            foreach (var it in items)
            {
                if (needsFilter && !MatchesCriteria(it, c, languages)) continue;
                if (seen.Add(it.Id)) results.Add(it);
            }

            page++;
            if (!needsFilter) break;
            if (page > Math.Min(total, TmdbMaxPage) || results.Count >= DiscoverTargetCount) break;
        }

        return new TmdbDiscoverResponseDTO
        {
            Results = results,
            Page = c.Page,
            NextPage = page <= Math.Min(total, TmdbMaxPage) ? page : null,
            TotalPages = total,
            TotalResults = totalHits,
            IsFiltered = needsFilter,
            Notes = notes
        };
    }

    private async Task<(List<TmdbDiscoverItemDTO> Items, int TotalPages, int TotalResults)> FetchDiscoverPageAsync(
        bool isTv, DiscoverCriteria c, int page)
    {
        var kind = isTv ? "tv" : "movie";
        var dateField = isTv ? "first_air_date" : "primary_release_date";
        var sortBy = c.Sort switch
        {
            "rating" => "vote_average.desc",
            "newest" => $"{dateField}.desc",
            "oldest" => $"{dateField}.asc",
            _ => "popularity.desc"
        };

        var q = new List<string>
        {
            $"api_key={_apiKey}",
            "language=vi-VN",
            "include_adult=false",
            $"page={page}",
            $"sort_by={sortBy}"
        };

        if (c.Genres.Count > 0) q.Add($"with_genres={string.Join(",", c.Genres)}"); // dấu phẩy = AND
        if (c.Country != null) q.Add($"with_origin_country={c.Country}");
        if (c.YearFrom.HasValue) q.Add($"{dateField}.gte={c.YearFrom.Value}-01-01");
        if (c.YearTo.HasValue) q.Add($"{dateField}.lte={c.YearTo.Value}-12-31");
        if (c.MinRating.HasValue)
            q.Add($"vote_average.gte={c.MinRating.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        // Xếp theo điểm mà không chặn số vote thì top toàn phim 1–2 vote điểm 10 → mặc định ≥ 100 vote
        var minVotes = c.MinVotes ?? (c.Sort == "rating" ? 100 : (int?)null);
        if (minVotes.HasValue) q.Add($"vote_count.gte={minVotes.Value}");

        return await ReadDiscoverPageAsync(isTv, $"{_baseUrl}/discover/{kind}?{string.Join("&", q)}");
    }

    private async Task<(List<TmdbDiscoverItemDTO> Items, int TotalPages, int TotalResults)> FetchSearchPageAsync(
        bool isTv, DiscoverCriteria c, int page)
    {
        var kind = isTv ? "tv" : "movie";
        var url =
            $"{_baseUrl}/search/{kind}?api_key={_apiKey}&query={Uri.EscapeDataString(c.Query!)}" +
            $"&page={page}&language=vi-VN&include_adult=false";

        if (c.YearFrom.HasValue && c.YearFrom == c.YearTo)
            url += isTv ? $"&first_air_date_year={c.YearFrom.Value}" : $"&primary_release_year={c.YearFrom.Value}";

        return await ReadDiscoverPageAsync(isTv, url);
    }

    private async Task<(List<TmdbDiscoverItemDTO> Items, int TotalPages, int TotalResults)> ReadDiscoverPageAsync(
        bool isTv, string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        if (isTv)
        {
            var tv = JsonSerializer.Deserialize<TmdbTvSearchResponseDTO>(content, _jsonOptions)
                     ?? new TmdbTvSearchResponseDTO();
            return (tv.Results.Select(x => TvToDiscoverItem(x)).ToList(), tv.TotalPages, tv.TotalResults);
        }

        var movies = JsonSerializer.Deserialize<TmdbSearchResponseDTO>(content, _jsonOptions)
                     ?? new TmdbSearchResponseDTO();
        return (movies.Results.Select(x => MovieToDiscoverItem(x)).ToList(), movies.TotalPages, movies.TotalResults);
    }

    private TmdbDiscoverItemDTO MovieToDiscoverItem(TmdbMovieDTO m) => new()
    {
        Id = m.Id,
        Title = m.Title,
        Name = m.Title,
        Overview = m.Overview,
        ReleaseDate = m.ReleaseDate,
        PosterUrl = BuildImageUrl(m.PosterPath),
        BackdropUrl = BuildImageUrl(m.BackdropPath, "w780"),
        VoteAverage = m.VoteAverage,
        VoteCount = m.VoteCount,
        Popularity = m.Popularity,
        OriginalLanguage = m.OriginalLanguage,
        GenreIds = m.GenreIds,
        OriginCountry = m.OriginCountry
    };

    private TmdbDiscoverItemDTO TvToDiscoverItem(TmdbTvDTO tv) => new()
    {
        Id = tv.Id,
        Title = tv.Name,
        Name = tv.Name,
        Overview = tv.Overview,
        FirstAirDate = tv.FirstAirDate,
        PosterUrl = BuildImageUrl(tv.PosterPath),
        BackdropUrl = BuildImageUrl(tv.BackdropPath, "w780"),
        VoteAverage = tv.VoteAverage,
        VoteCount = tv.VoteCount,
        Popularity = tv.Popularity,
        OriginalLanguage = tv.OriginalLanguage,
        GenreIds = tv.GenreIds,
        OriginCountry = tv.OriginCountry
    };

    private static bool MatchesCriteria(TmdbDiscoverItemDTO it, DiscoverCriteria c, string[]? languages)
    {
        if (c.Genres.Count > 0 && !c.Genres.All(g => it.GenreIds.Contains(g))) return false;
        if (c.MinRating.HasValue && it.VoteAverage < c.MinRating.Value) return false;
        if (c.MinVotes.HasValue && it.VoteCount < c.MinVotes.Value) return false;

        if (c.YearFrom.HasValue || c.YearTo.HasValue)
        {
            var date = !string.IsNullOrEmpty(it.ReleaseDate) ? it.ReleaseDate : it.FirstAirDate;
            if (string.IsNullOrEmpty(date) || date.Length < 4 || !int.TryParse(date.AsSpan(0, 4), out var year))
                return false;
            if (c.YearFrom.HasValue && year < c.YearFrom.Value) return false;
            if (c.YearTo.HasValue && year > c.YearTo.Value) return false;
        }

        if (c.Country != null)
        {
            if (it.OriginCountry.Count > 0)
            {
                if (!it.OriginCountry.Contains(c.Country, StringComparer.OrdinalIgnoreCase)) return false;
            }
            else if (languages != null)
            {
                if (string.IsNullOrEmpty(it.OriginalLanguage)
                    || !languages.Contains(it.OriginalLanguage, StringComparer.OrdinalIgnoreCase))
                    return false;
            }
        }

        return true;
    }

    // ════════════════════════════════════════════════════════════════════════
    // TRANSLATION HELPERS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dịch bất kỳ đoạn text tiếng Anh nào sang tiếng Việt.
    /// Dùng chung cho biography, episode overview, season overview.
    /// Provider được cấu hình qua appsettings Translation:Provider.
    /// </summary>
    // Giới hạn số request dịch chạy cùng lúc (1 season có thể có hàng chục tập cần dịch)
    private static readonly SemaphoreSlim _translateGate = new(4);

    private async Task<string> TranslateTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var provider = _configuration["Translation:Provider"] ?? "Google";

        await _translateGate.WaitAsync();
        try
        {
            return provider switch
            {
                "GoogleCloud" => await TranslateWithGoogleCloudAsync(text),
                "Claude" => await TranslateWithClaudeAsync(text),
                "Groq" => await TranslateWithGroqAsync(text),
                "None" => text,
                _ => await TranslateWithGoogleFreeAsync(text)
            };
        }
        catch (Exception ex)
        {
            // Fallback về text gốc nếu dịch thất bại — không crash, nhưng phải để lại log
            _logger?.LogWarning(ex, "Translate ({Provider}) thất bại, giữ nguyên text gốc", provider);
            return text;
        }
        finally
        {
            _translateGate.Release();
        }
    }

    // Endpoint Google miễn phí (client=gtx) giới hạn rất gắt: quá nhiều request là trả 429 / redirect sang
    // google.com/sorry (captcha). Nên: gọi tuần tự, nghỉ giữa các lần, và khi bị chặn thì tạm dừng hẳn
    // một lúc (giữ nguyên text gốc) thay vì tiếp tục dội request làm IP bị chặn lâu hơn.
    private static readonly SemaphoreSlim _googleFreeGate = new(1);
    private static DateTime _googleFreeBlockedUntil = DateTime.MinValue;

    private async Task<string> TranslateWithGoogleFreeAsync(string text)
    {
        if (DateTime.UtcNow < _googleFreeBlockedUntil) return text;

        if (text.Length > 4500)
            text = text[..4500] + "...";

        var encoded = Uri.EscapeDataString(text);
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=vi&dt=t&q={encoded}";

        await _googleFreeGate.WaitAsync();
        try
        {
            if (DateTime.UtcNow < _googleFreeBlockedUntil) return text;

            var response = await _translateClient.GetAsync(url);
            var code = (int)response.StatusCode;
            if (code == 429 || (code >= 300 && code < 400))
            {
                _googleFreeBlockedUntil = DateTime.UtcNow.AddMinutes(15);
                throw new HttpRequestException(
                    $"Google Translate (free) bị giới hạn ({code}) — tạm dừng 15 phút. " +
                    "Hãy đổi Translation:Provider sang GoogleCloud hoặc Claude.");
            }
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            await Task.Delay(300); // nghỉ giữa các request để tránh bị 429

            var parsed = JsonSerializer.Deserialize<JsonElement>(json);

            var sb = new StringBuilder();
            var outerArray = parsed[0];
            foreach (var segment in outerArray.EnumerateArray())
            {
                var translated = segment[0].GetString();
                if (!string.IsNullOrEmpty(translated))
                    sb.Append(translated);
            }

            var result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? text : result;
        }
        finally
        {
            _googleFreeGate.Release();
        }
    }

    private async Task<string> TranslateWithGoogleCloudAsync(string text)
    {
        var apiKey = _configuration["Translation:GoogleCloudApiKey"]
                     ?? throw new InvalidOperationException("Translation:GoogleCloudApiKey chưa được cấu hình.");

        var url = $"https://translation.googleapis.com/language/translate/v2?key={apiKey}";
        var payload = JsonSerializer.Serialize(new
        {
            q = text,
            source = "en",
            target = "vi",
            format = "text"
        });

        var response = await _translateClient.PostAsync(url,
            new StringContent(payload, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        var translated = parsed
            .GetProperty("data")
            .GetProperty("translations")[0]
            .GetProperty("translatedText")
            .GetString();

        return string.IsNullOrWhiteSpace(translated) ? text : translated;
    }

    private async Task<string> TranslateWithClaudeAsync(string text)
    {
        var apiKey = _configuration["Translation:AnthropicApiKey"]
                     ?? throw new InvalidOperationException("Translation:AnthropicApiKey chưa được cấu hình.");

        var payload = JsonSerializer.Serialize(new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 1024,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content =
                        $"Dịch đoạn văn sau sang tiếng Việt tự nhiên. Chỉ trả về bản dịch, không giải thích thêm:\n\n{text}"
                }
            }
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _translateClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        var translated = parsed
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        return string.IsNullOrWhiteSpace(translated) ? text : translated.Trim();
    }

    // ── Dịch theo lô ─────────────────────────────────────────────────────────

    /// <summary>
    /// Dịch nhiều đoạn một lúc. Provider Groq gộp tối đa 20 đoạn / 1 request để tiết kiệm hạn mức;
    /// provider khác dịch song song từng đoạn như cũ. Luôn trả về danh sách cùng độ dài, lỗi thì giữ text gốc.
    /// </summary>
    private async Task<List<string>> TranslateManyAsync(IReadOnlyList<string> texts)
    {
        if (texts.Count == 0) return new();

        var provider = _configuration["Translation:Provider"] ?? "Google";
        if (provider != "Groq" || texts.Count == 1)
            return (await Task.WhenAll(texts.Select(TranslateTextAsync))).ToList();

        var results = texts.ToList();
        var offset = 0;
        foreach (var chunk in texts.Chunk(20))
        {
            List<string>? translated = null;
            await _translateGate.WaitAsync();
            try
            {
                translated = await TranslateBatchWithGroqAsync(chunk);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Groq dịch theo lô thất bại, giữ nguyên text gốc ({Count} đoạn)", chunk.Length);
                offset += chunk.Length;
                continue;
            }
            finally
            {
                _translateGate.Release();
            }

            if (translated == null)
            {
                // Model trả sai số phần tử → dịch lẻ từng đoạn cho lô này
                for (var i = 0; i < chunk.Length; i++)
                    results[offset + i] = await TranslateTextAsync(chunk[i]);
            }
            else
            {
                for (var i = 0; i < chunk.Length; i++)
                    if (!string.IsNullOrWhiteSpace(translated[i]))
                        results[offset + i] = translated[i];
            }

            offset += chunk.Length;
        }

        return results;
    }

    // ── Groq (API tương thích OpenAI) ────────────────────────────────────────

    private const string GroqChatUrl = "https://api.groq.com/openai/v1/chat/completions";

    private const string GroqSingleSystemPrompt =
        "Bạn là dịch giả phim ảnh. Dịch văn bản người dùng gửi sang tiếng Việt tự nhiên, trôi chảy. " +
        "Giữ nguyên tên riêng (người, địa danh). Chỉ trả về bản dịch: không giải thích, không ghi chú, " +
        "không thêm dấu ngoặc kép bao quanh.";

    private const string GroqBatchSystemPrompt =
        "Bạn là dịch giả phim ảnh. Người dùng gửi một JSON dạng {\"texts\":[...]}. Dịch TỪNG phần tử sang " +
        "tiếng Việt tự nhiên, giữ nguyên tên riêng. Trả về đúng một JSON object dạng {\"translations\":[...]} " +
        "có CÙNG số phần tử và CÙNG thứ tự. Không giải thích, không thêm gì ngoài JSON.";

    private async Task<string> TranslateWithGroqAsync(string text)
    {
        if (text.Length > 4500)
            text = text[..4500] + "...";

        var result = (await CallGroqChatAsync(GroqSingleSystemPrompt, text, 2048, jsonMode: false)).Trim();
        return string.IsNullOrWhiteSpace(result) ? text : result;
    }

    /// <summary>Trả null nếu model trả JSON sai cấu trúc hoặc sai số phần tử.</summary>
    private async Task<List<string>?> TranslateBatchWithGroqAsync(IReadOnlyList<string> texts)
    {
        var userJson = JsonSerializer.Serialize(new { texts });
        var content = await CallGroqChatAsync(GroqBatchSystemPrompt, userJson, 4096, jsonMode: true);

        using var doc = JsonDocument.Parse(content);
        if (!doc.RootElement.TryGetProperty("translations", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var list = arr.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
        return list.Count == texts.Count ? list : null;
    }

    private async Task<string> CallGroqChatAsync(string system, string user, int maxTokens, bool jsonMode)
    {
        var apiKey = _configuration["Translation:GroqApiKey"] ?? _configuration["Groq:ApiKey"]
                     ?? throw new InvalidOperationException(
                         "Chưa cấu hình Translation:GroqApiKey (hoặc Groq:ApiKey).");
        var model = _configuration["Translation:GroqModel"] ?? "llama-3.3-70b-versatile";

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["temperature"] = 0.2,
            ["max_tokens"] = maxTokens,
            ["messages"] = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };
        if (jsonMode) body["response_format"] = new { type = "json_object" };
        var payload = JsonSerializer.Serialize(body);

        for (var attempt = 1;; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, GroqChatUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await _translateClient.SendAsync(request);

            if ((int)response.StatusCode == 429)
            {
                // Chạm giới hạn tốc độ: chờ theo Retry-After rồi thử lại; nếu phải chờ lâu (hết hạn mức ngày) thì bỏ
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(3 * attempt);
                if (attempt >= 3 || wait > TimeSpan.FromSeconds(20))
                    throw new HttpRequestException($"Groq 429 (giới hạn tốc độ/hạn mức), Retry-After = {wait.TotalSeconds:0}s");
                await Task.Delay(wait + TimeSpan.FromMilliseconds(200));
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Groq {(int)response.StatusCode}: {err}");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        }
    }

    private string BuildImageUrl(string? path, string size = "w500")
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        return $"https://image.tmdb.org/t/p/{size}{path}";
    }
}

/// <summary>
/// Retry tự động cho request GET tới TMDB khi gặp lỗi mạng tạm thời
/// (SSL handshake bị reset, timeout, 5xx, 429). Không cần thêm NuGet package.
/// Chỉ retry GET (idempotent); POST (VD: dịch bằng Claude) đi thẳng, không retry.
/// </summary>
public class TmdbRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 4; // 1 lần đầu + 3 lần retry

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
            return await base.SendAsync(request, cancellationToken);

        for (var attempt = 1;; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);

                var code = (int)response.StatusCode;
                var transient = code == 429 || code >= 500;
                if (!transient || attempt == MaxAttempts) return response;

                var delay = GetDelay(attempt, response);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(GetDelay(attempt, null), cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                // timeout của HttpClient (không phải client chủ động huỷ request)
                await Task.Delay(GetDelay(attempt, null), cancellationToken);
            }
        }
    }

    private static TimeSpan GetDelay(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter?.Delta;
        if (retryAfter.HasValue && retryAfter.Value < TimeSpan.FromSeconds(10))
            return retryAfter.Value;

        // 1s, 2s, 4s + jitter nhỏ để các request không retry cùng lúc
        var seconds = Math.Pow(2, attempt - 1);
        return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 300));
    }
}
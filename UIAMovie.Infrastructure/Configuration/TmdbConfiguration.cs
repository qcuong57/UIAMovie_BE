// UIAMovie.Infrastructure/Configuration/TmdbService.cs

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Infrastructure.Configuration;

public interface ITmdbService
{
    // ── Movie ─────────────────────────────────────────────────────────────────
    Task<TmdbMovieDetailDTO?>     GetMovieAsync(int tmdbId);
    Task<TmdbSearchResponseDTO>   SearchMoviesAsync(string query, int page = 1);
    Task<TmdbSearchResponseDTO>   GetTrendingMoviesAsync(string timeWindow = "week");
    Task<List<TmdbTrailerDTO>>    GetMovieTrailersAsync(int tmdbId);
    Task<List<TmdbGenreDTO>>      GetGenresAsync();
    Task<TmdbCreditsResponseDTO?> GetCreditsAsync(int tmdbId);
    Task<TmdbImagesResponseDTO?>  GetImagesAsync(int tmdbId);
    Task<TmdbPersonDetailDTO?>    GetPersonDetailAsync(int tmdbPersonId);
    Task<List<string>>            GetPersonImagesAsync(int tmdbPersonId);
    Task<TmdbFullMovieDTO?>       GetFullMovieAsync(int tmdbId);

    // ── TV Show ───────────────────────────────────────────────────────────────
    Task<TmdbTvDetailDTO?>          GetTvShowAsync(int tmdbId);
    Task<TmdbTvSearchResponseDTO>   SearchTvShowsAsync(string query, int page = 1);
    Task<TmdbTvSearchResponseDTO>   GetTrendingTvShowsAsync(string timeWindow = "week");
    Task<List<TmdbTrailerDTO>>      GetTvTrailersAsync(int tmdbId);
    Task<TmdbTvCreditsResponseDTO?> GetTvCreditsAsync(int tmdbId);
    Task<TmdbImagesResponseDTO?>    GetTvImagesAsync(int tmdbId);
    Task<List<TmdbGenreDTO>>        GetTvGenresAsync();
    Task<TmdbSeasonDetailDTO?>      GetTvSeasonDetailAsync(int tmdbId, int seasonNumber);
    Task<TmdbFullTvShowDTO?>        GetFullTvShowAsync(int tmdbId);
}

public class TmdbService : ITmdbService
{
    private readonly HttpClient      _httpClient;
    private readonly string          _apiKey;
    private readonly string          _baseUrl;
    private readonly IConfiguration  _configuration;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public TmdbService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient    = httpClientFactory.CreateClient();
        _apiKey        = configuration["TMDB:ApiKey"]!;
        _baseUrl       = configuration["TMDB:BaseUrl"]!;
        _configuration = configuration;
    }

    // ════════════════════════════════════════════════════════════════════════
    // MOVIE METHODS — giữ nguyên, không thay đổi
    // ════════════════════════════════════════════════════════════════════════

    public async Task<TmdbMovieDetailDTO?> GetMovieAsync(int tmdbId)
    {
        var url      = $"{_baseUrl}/movie/{tmdbId}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var movie   = JsonSerializer.Deserialize<TmdbMovieDetailDTO>(content, _jsonOptions);

        if (movie != null)
        {
            movie.PosterUrl   = BuildImageUrl(movie.PosterPath);
            movie.BackdropUrl = BuildImageUrl(movie.BackdropPath, "original");
        }

        return movie;
    }

    public async Task<TmdbSearchResponseDTO> SearchMoviesAsync(string query, int page = 1)
    {
        var url      = $"{_baseUrl}/search/movie?api_key={_apiKey}&query={Uri.EscapeDataString(query)}&page={page}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbSearchResponseDTO>(content, _jsonOptions)
                      ?? new TmdbSearchResponseDTO();

        foreach (var m in result.Results)
        {
            m.PosterUrl   = BuildImageUrl(m.PosterPath);
            m.BackdropUrl = BuildImageUrl(m.BackdropPath, "original");
        }

        return result;
    }

    public async Task<TmdbSearchResponseDTO> GetTrendingMoviesAsync(string timeWindow = "week")
    {
        var url      = $"{_baseUrl}/trending/movie/{timeWindow}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbSearchResponseDTO>(content, _jsonOptions)
                      ?? new TmdbSearchResponseDTO();

        foreach (var m in result.Results)
        {
            m.PosterUrl   = BuildImageUrl(m.PosterPath);
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
        var url      = $"{_baseUrl}/genre/movie/list?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbGenreResponseDTO>(content, _jsonOptions);

        return result?.Genres ?? new();
    }

    public async Task<TmdbCreditsResponseDTO?> GetCreditsAsync(int tmdbId)
    {
        var url      = $"{_baseUrl}/movie/{tmdbId}/credits?api_key={_apiKey}&language=en-US";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbCreditsResponseDTO>(content, _jsonOptions);

        if (result != null)
        {
            foreach (var c in result.Cast) c.ProfileUrl = BuildImageUrl(c.ProfilePath);
            foreach (var c in result.Crew) c.ProfileUrl = BuildImageUrl(c.ProfilePath);
        }

        return result;
    }

    public async Task<TmdbImagesResponseDTO?> GetImagesAsync(int tmdbId)
    {
        var url      = $"{_baseUrl}/movie/{tmdbId}/images?api_key={_apiKey}";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbImagesResponseDTO>(content, _jsonOptions);

        if (result != null)
        {
            foreach (var img in result.Backdrops) img.Url = BuildImageUrl(img.FilePath, "original");
            foreach (var img in result.Posters)   img.Url = BuildImageUrl(img.FilePath, "w500");
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
        var url      = $"{_baseUrl}/person/{tmdbPersonId}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbPersonDetailDTO>(content, _jsonOptions);
        if (result == null) return null;

        if (string.IsNullOrWhiteSpace(result.Biography))
        {
            var enUrl      = $"{_baseUrl}/person/{tmdbPersonId}?api_key={_apiKey}&language=en-US";
            var enResponse = await _httpClient.GetAsync(enUrl);

            if (enResponse.IsSuccessStatusCode)
            {
                var enContent = await enResponse.Content.ReadAsStringAsync();
                var enResult  = JsonSerializer.Deserialize<TmdbPersonDetailDTO>(enContent, _jsonOptions);

                if (enResult != null && !string.IsNullOrWhiteSpace(enResult.Biography))
                    result.Biography = await TranslateTextAsync(enResult.Biography);
            }
        }

        result.ProfileUrl = BuildImageUrl(result.ProfilePath);
        return result;
    }

    public async Task<List<string>> GetPersonImagesAsync(int tmdbPersonId)
    {
        var url      = $"{_baseUrl}/person/{tmdbPersonId}/images?api_key={_apiKey}";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return new();

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbPersonImagesResponseDTO>(content, _jsonOptions);

        return result?.Profiles
            .OrderByDescending(p => p.VoteAverage)
            .Take(5)
            .Select(p => BuildImageUrl(p.FilePath, "w500"))
            .Where(u => !string.IsNullOrEmpty(u))
            .ToList() ?? new();
    }

    public async Task<TmdbFullMovieDTO?> GetFullMovieAsync(int tmdbId)
    {
        var detailTask   = GetMovieAsync(tmdbId);
        var creditsTask  = GetCreditsAsync(tmdbId);
        var imagesTask   = GetImagesAsync(tmdbId);
        var trailersTask = GetMovieTrailersAsync(tmdbId);

        await Task.WhenAll(detailTask, creditsTask, imagesTask, trailersTask);

        var detail = detailTask.Result;
        if (detail == null) return null;

        var credits  = creditsTask.Result;
        var images   = imagesTask.Result;
        var trailers = trailersTask.Result;

        var top10Cast = credits?.Cast.OrderBy(c => c.Order).Take(10).ToList() ?? new();
        var director  = credits?.Crew.FirstOrDefault(c => c.Job == "Director");

        var personIds = top10Cast.Select(c => c.Id).ToList();
        if (director != null) personIds.Add(director.Id);
        var distinctPersonIds = personIds.Distinct().ToList();

        var personDetailTasks = distinctPersonIds.ToDictionary(id => id, id => GetPersonDetailAsync(id));
        var personImageTasks  = distinctPersonIds.ToDictionary(id => id, id => GetPersonImagesAsync(id));

        await Task.WhenAll(personDetailTasks.Values.Concat<Task>(personImageTasks.Values));

        return new TmdbFullMovieDTO
        {
            Detail    = detail,
            Cast      = top10Cast,
            Director  = director,
            Backdrops = images?.Backdrops.OrderByDescending(i => i.VoteAverage).Take(5).ToList() ?? new(),
            Posters   = images?.Posters.OrderByDescending(i => i.VoteAverage).Take(3).ToList() ?? new(),
            Trailers  = trailers,
            PersonDetails = personDetailTasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result),
            PersonImages  = personImageTasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result)
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
        var url      = $"{_baseUrl}/tv/{tmdbId}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var show    = JsonSerializer.Deserialize<TmdbTvDetailDTO>(content, _jsonOptions);
        if (show == null) return null;

        show.PosterUrl   = BuildImageUrl(show.PosterPath);
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
        var url      = $"{_baseUrl}/search/tv?api_key={_apiKey}&query={Uri.EscapeDataString(query)}&page={page}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbTvSearchResponseDTO>(content, _jsonOptions)
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
        var url      = $"{_baseUrl}/trending/tv/{timeWindow}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbTvSearchResponseDTO>(content, _jsonOptions)
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
        var url      = $"{_baseUrl}/tv/{tmdbId}/aggregate_credits?api_key={_apiKey}&language=en-US";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbTvCreditsResponseDTO>(content, _jsonOptions);

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
        var url      = $"{_baseUrl}/tv/{tmdbId}/images?api_key={_apiKey}";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbImagesResponseDTO>(content, _jsonOptions);

        if (result != null)
        {
            foreach (var img in result.Backdrops) img.Url = BuildImageUrl(img.FilePath, "original");
            foreach (var img in result.Posters)   img.Url = BuildImageUrl(img.FilePath, "w500");
        }

        return result;
    }

    /// <summary>
    /// Lấy danh sách genre dành riêng cho TV.
    /// </summary>
    public async Task<List<TmdbGenreDTO>> GetTvGenresAsync()
    {
        var url      = $"{_baseUrl}/genre/tv/list?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbGenreResponseDTO>(content, _jsonOptions);

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
    public async Task<TmdbSeasonDetailDTO?> GetTvSeasonDetailAsync(int tmdbId, int seasonNumber)
    {
        // ── Bước 1: Lấy bản vi-VN ────────────────────────────────────────────
        var url      = $"{_baseUrl}/tv/{tmdbId}/season/{seasonNumber}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbSeasonDetailDTO>(content, _jsonOptions);
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
            var enUrl      = $"{_baseUrl}/tv/{tmdbId}/season/{seasonNumber}?api_key={_apiKey}&language=en-US";
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

        // Dịch song song tất cả episode cần dịch — tránh gọi translate tuần tự
        var translateTasks = result.Episodes
            .Select(async ep =>
            {
                if (!string.IsNullOrWhiteSpace(ep.Overview)) return; // đã có tiếng Việt

                enEpisodeMap.TryGetValue(ep.EpisodeNumber, out var enOverview);
                if (string.IsNullOrWhiteSpace(enOverview)) return;   // en cũng trống

                ep.Overview = await TranslateTextAsync(enOverview);
            });

        await Task.WhenAll(translateTasks);

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
        var detailTask   = GetTvShowAsync(tmdbId);
        var creditsTask  = GetTvCreditsAsync(tmdbId);
        var imagesTask   = GetTvImagesAsync(tmdbId);
        var trailersTask = GetTvTrailersAsync(tmdbId);

        await Task.WhenAll(detailTask, creditsTask, imagesTask, trailersTask);

        var detail = detailTask.Result;
        if (detail == null) return null;

        var credits  = creditsTask.Result;
        var images   = imagesTask.Result;
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
        var personImageTasks  = distinctPersonIds.ToDictionary(id => id, id => GetPersonImagesAsync(id));

        await Task.WhenAll(personDetailTasks.Values.Concat<Task>(personImageTasks.Values));

        return new TmdbFullTvShowDTO
        {
            Detail        = detail,
            Cast          = top10Cast,
            Director      = director,
            Backdrops     = images?.Backdrops.OrderByDescending(i => i.VoteAverage).Take(5).ToList() ?? new(),
            Posters       = images?.Posters.OrderByDescending(i => i.VoteAverage).Take(3).ToList() ?? new(),
            Trailers      = trailers,
            SeasonDetails = seasonDetails,
            PersonDetails = personDetailTasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result),
            PersonImages  = personImageTasks.ToDictionary(kv => kv.Key, kv => kv.Value.Result)
        };
    }

    // ════════════════════════════════════════════════════════════════════════
    // SHARED PRIVATE HELPERS
    // ════════════════════════════════════════════════════════════════════════

    private async Task<List<TmdbTrailerDTO>> FetchTrailersAsync(string baseVideoUrl, string? language)
    {
        var langParam = language != null ? $"&language={language}" : string.Empty;
        var url       = $"{baseVideoUrl}?api_key={_apiKey}{langParam}";
        var response  = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode) return new();

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbVideoResponseDTO>(content, _jsonOptions);

        return result?.Results
            .Where(v => v.Type == "Trailer")
            .Select(v => new TmdbTrailerDTO
            {
                Key        = v.Key,
                Name       = v.Name,
                Type       = v.Type,
                YoutubeUrl = $"https://www.youtube.com/watch?v={v.Key}"
            })
            .ToList() ?? new();
    }

    private void NormalizeTvItem(TmdbTvDTO tv)
    {
        tv.PosterUrl   = BuildImageUrl(tv.PosterPath);
        tv.BackdropUrl = BuildImageUrl(tv.BackdropPath, "original");
    }

    // ════════════════════════════════════════════════════════════════════════
    // TRANSLATION HELPERS
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dịch bất kỳ đoạn text tiếng Anh nào sang tiếng Việt.
    /// Dùng chung cho biography, episode overview, season overview.
    /// Provider được cấu hình qua appsettings Translation:Provider.
    /// </summary>
    private async Task<string> TranslateTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var provider = _configuration["Translation:Provider"] ?? "Google";

        try
        {
            return provider switch
            {
                "GoogleCloud" => await TranslateWithGoogleCloudAsync(text),
                "Claude"      => await TranslateWithClaudeAsync(text),
                "None"        => text,
                _             => await TranslateWithGoogleFreeAsync(text)
            };
        }
        catch
        {
            // Fallback về text gốc nếu dịch thất bại — không crash
            return text;
        }
    }

    private async Task<string> TranslateWithGoogleFreeAsync(string text)
    {
        if (text.Length > 4500)
            text = text[..4500] + "...";

        var encoded  = Uri.EscapeDataString(text);
        var url      = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=vi&dt=t&q={encoded}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json    = await response.Content.ReadAsStringAsync();
        var parsed  = JsonSerializer.Deserialize<JsonElement>(json);

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

    private async Task<string> TranslateWithGoogleCloudAsync(string text)
    {
        var apiKey = _configuration["Translation:GoogleCloudApiKey"]
            ?? throw new InvalidOperationException("Translation:GoogleCloudApiKey chưa được cấu hình.");

        var url     = $"https://translation.googleapis.com/language/translate/v2?key={apiKey}";
        var payload = JsonSerializer.Serialize(new
        {
            q      = text,
            source = "en",
            target = "vi",
            format = "text"
        });

        var response = await _httpClient.PostAsync(url,
            new StringContent(payload, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        var json   = await response.Content.ReadAsStringAsync();
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
            model      = "claude-haiku-4-5-20251001",
            max_tokens = 1024,
            messages   = new[]
            {
                new
                {
                    role    = "user",
                    content = $"Dịch đoạn văn sau sang tiếng Việt tự nhiên. Chỉ trả về bản dịch, không giải thích thêm:\n\n{text}"
                }
            }
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json   = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        var translated = parsed
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        return string.IsNullOrWhiteSpace(translated) ? text : translated.Trim();
    }

    private string BuildImageUrl(string? path, string size = "w500")
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        return $"https://image.tmdb.org/t/p/{size}{path}";
    }
}
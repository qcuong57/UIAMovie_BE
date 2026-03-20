// UIAMovie.Infrastructure/Configuration/TmdbService.cs

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Infrastructure.Configuration;

public interface ITmdbService
{
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

    // ── Existing methods ──────────────────────────────────────────────────────

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
        // Bước 1: Thử vi-VN
        var trailers = await FetchTrailersAsync(tmdbId, "vi-VN");
        if (trailers.Any()) return trailers;

        // Bước 2: Fallback en-US
        trailers = await FetchTrailersAsync(tmdbId, "en-US");
        if (trailers.Any()) return trailers;

        // Bước 3: Không lọc ngôn ngữ — lấy tất cả
        return await FetchTrailersAsync(tmdbId, null);
    }

    /// <summary>
    /// Gọi TMDB /videos với language tuỳ chọn, trả về danh sách Trailer.
    /// language = null → không truyền tham số language (lấy tất cả).
    /// </summary>
    private async Task<List<TmdbTrailerDTO>> FetchTrailersAsync(int tmdbId, string? language)
    {
        var langParam = language != null ? $"&language={language}" : string.Empty;
        var url       = $"{_baseUrl}/movie/{tmdbId}/videos?api_key={_apiKey}{langParam}";
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
        // Dùng en-US để tên diễn viên / đạo diễn luôn là dạng latinh
        // (vd: "Go Youn-jung" thay vì "고윤정")
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

    // ── GetPersonDetailAsync — fallback vi-VN → en-US → tự dịch sang tiếng Việt

    /// <summary>
    /// 1. Lấy bio tiếng Việt từ TMDB.
    /// 2. Nếu trống → lấy bio tiếng Anh.
    /// 3. Nếu vẫn tiếng Anh → tự động dịch sang tiếng Việt theo provider cấu hình.
    ///
    /// Config trong appsettings.json:
    ///   "Translation:Provider": "Google"      → Google Translate (miễn phí, không cần key)
    ///   "Translation:Provider": "GoogleCloud" → Google Cloud API (cần key)
    ///   "Translation:Provider": "Claude"      → Claude AI (cần Anthropic API key)
    ///   "Translation:Provider": "None"        → không dịch, giữ tiếng Anh
    /// </summary>
    public async Task<TmdbPersonDetailDTO?> GetPersonDetailAsync(int tmdbPersonId)
    {
        // Bước 1: Thử vi-VN trước
        var url      = $"{_baseUrl}/person/{tmdbPersonId}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var result  = JsonSerializer.Deserialize<TmdbPersonDetailDTO>(content, _jsonOptions);
        if (result == null) return null;

        // Bước 2: Bio tiếng Việt trống → lấy tiếng Anh
        if (string.IsNullOrWhiteSpace(result.Biography))
        {
            var enUrl      = $"{_baseUrl}/person/{tmdbPersonId}?api_key={_apiKey}&language=en-US";
            var enResponse = await _httpClient.GetAsync(enUrl);

            if (enResponse.IsSuccessStatusCode)
            {
                var enContent = await enResponse.Content.ReadAsStringAsync();
                var enResult  = JsonSerializer.Deserialize<TmdbPersonDetailDTO>(enContent, _jsonOptions);

                if (enResult != null && !string.IsNullOrWhiteSpace(enResult.Biography))
                {
                    // Bước 3: Dịch tiếng Anh → tiếng Việt
                    result.Biography = await TranslateBiographyAsync(enResult.Biography);
                }
            }
        }

        result.ProfileUrl = BuildImageUrl(result.ProfilePath);
        return result;
    }

    // ── GetPersonImagesAsync ──────────────────────────────────────────────────

    /// <summary>Lấy danh sách ảnh profile của một người từ TMDB. Tối đa 5 ảnh.</summary>
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

    // ── GetFullMovieAsync ─────────────────────────────────────────────────────

    public async Task<TmdbFullMovieDTO?> GetFullMovieAsync(int tmdbId)
    {
        // Bước 1: Gọi song song 4 movie endpoints
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

        // Bước 2: Gọi song song detail + ảnh cho từng người
        var personDetailTasks = distinctPersonIds
            .ToDictionary(id => id, id => GetPersonDetailAsync(id));

        var personImageTasks = distinctPersonIds
            .ToDictionary(id => id, id => GetPersonImagesAsync(id));

        await Task.WhenAll(
            personDetailTasks.Values.Concat<Task>(personImageTasks.Values));

        return new TmdbFullMovieDTO
        {
            Detail    = detail,
            Cast      = top10Cast,
            Director  = director,
            Backdrops = images?.Backdrops.OrderByDescending(i => i.VoteAverage).Take(5).ToList() ?? new(),
            Posters   = images?.Posters.OrderByDescending(i => i.VoteAverage).Take(3).ToList() ?? new(),
            Trailers  = trailers,
            PersonDetails = personDetailTasks
                .ToDictionary(kv => kv.Key, kv => kv.Value.Result),
            PersonImages = personImageTasks
                .ToDictionary(kv => kv.Key, kv => kv.Value.Result)
        };
    }

    // ── Translation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Dịch biography sang tiếng Việt theo provider được cấu hình.
    /// Nếu dịch thất bại vì bất kỳ lý do gì → trả về text gốc (tiếng Anh).
    /// </summary>
    private async Task<string> TranslateBiographyAsync(string text)
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
                _             => await TranslateWithGoogleFreeAsync(text)  // mặc định
            };
        }
        catch
        {
            return text;
        }
    }

    // ── Provider 1: Google Translate (miễn phí, không cần key) ───────────────

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

    // ── Provider 2: Google Cloud Translation API ──────────────────────────────

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

    // ── Provider 3: Claude AI ─────────────────────────────────────────────────

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
                    content = $"Dịch đoạn tiểu sử diễn viên sau sang tiếng Việt. Chỉ trả về bản dịch, không giải thích thêm:\n\n{text}"
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

    // ── Helper ────────────────────────────────────────────────────────────────

    private string BuildImageUrl(string? path, string size = "w500")
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        return $"https://image.tmdb.org/t/p/{size}{path}";
    }
}
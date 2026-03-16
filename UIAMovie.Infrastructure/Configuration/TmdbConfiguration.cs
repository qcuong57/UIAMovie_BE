// UIAMovie.Infrastructure/Configuration/TmdbService.cs

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Infrastructure.Configuration;

public interface ITmdbService
{
    Task<TmdbMovieDetailDTO?> GetMovieAsync(int tmdbId);
    Task<TmdbSearchResponseDTO> SearchMoviesAsync(string query, int page = 1);
    Task<TmdbSearchResponseDTO> GetTrendingMoviesAsync(string timeWindow = "week");
    Task<List<TmdbTrailerDTO>> GetMovieTrailersAsync(int tmdbId);
    Task<List<TmdbGenreDTO>> GetGenresAsync();
}

public class TmdbService : ITmdbService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    // ✅ Không cần PropertyNameCaseInsensitive vì đã dùng [JsonPropertyName] explicit trong DTOs
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public TmdbService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _apiKey = configuration["TMDB:ApiKey"]!;
        _baseUrl = configuration["TMDB:BaseUrl"]!;
    }

    public async Task<TmdbMovieDetailDTO?> GetMovieAsync(int tmdbId)
    {
        var url = $"{_baseUrl}/movie/{tmdbId}?api_key={_apiKey}&language=vi-VN";
        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var content = await response.Content.ReadAsStringAsync();
        var movie = JsonSerializer.Deserialize<TmdbMovieDetailDTO>(content, _jsonOptions);

        if (movie != null)
        {
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

    public async Task<List<TmdbTrailerDTO>> GetMovieTrailersAsync(int tmdbId)
    {
        var url = $"{_baseUrl}/movie/{tmdbId}/videos?api_key={_apiKey}&language=en-US";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbVideoResponseDTO>(content, _jsonOptions);

        return result?.Results
            .Where(v => v.Type == "Trailer" || v.Type == "Teaser")
            .Select(v => new TmdbTrailerDTO
            {
                Key = v.Key,
                Name = v.Name,
                Type = v.Type,
                YoutubeUrl = $"https://www.youtube.com/watch?v={v.Key}"
            })
            .ToList() ?? new();
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

    private string BuildImageUrl(string? path, string size = "w500")
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        return $"https://image.tmdb.org/t/p/{size}{path}";
    }
}
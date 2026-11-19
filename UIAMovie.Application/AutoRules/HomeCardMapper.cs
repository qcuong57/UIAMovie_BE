// UIAMovie.Application/Services/AutoRules/HomeCardMapper.cs
using System.Text.RegularExpressions;
using UIAMovie.Application.DTOs;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.AutoRules;

/// <summary>Entity → HomeContentCardDTO. Dùng chung cho HomeSectionService và các IAutoRule.</summary>
public static class HomeCardMapper
{
    public static HomeContentCardDTO ToCard(Movie m) => new()
    {
        ContentType = "movie",
        Id = m.Id, Title = m.Title, Description = m.Description,
        PosterUrl = m.PosterUrl, BackdropUrl = m.BackdropUrl, Rating = m.ImdbRating,
        ReleaseDate = m.ReleaseDate, OriginCountry = m.OriginCountry, IsPremium = m.IsPremium,
        IsUpcoming = m.ReleaseDate.HasValue && m.ReleaseDate.Value > DateTime.UtcNow,
        Genres = m.MovieGenres?.Select(g => g.Genre?.Name ?? "").Where(n => n != "").ToList() ?? new(),
        Duration = m.Duration,
        TrailerKey = m.MovieVideos?.Where(v => v.VideoType == "trailer" && !string.IsNullOrEmpty(v.VideoUrl))
            .Select(v => ExtractYoutubeKey(v.VideoUrl)).FirstOrDefault(k => k != null),
        TrailerVideoUrl = m.MovieVideos?.Where(v => v.VideoType == "trailer_upload" && !string.IsNullOrEmpty(v.VideoUrl))
            .Select(v => v.VideoUrl).FirstOrDefault()
    };

    public static HomeContentCardDTO ToCard(TvShow t) => new()
    {
        ContentType = "tvshow",
        Id = t.Id, Title = t.Title, Description = t.Description,
        PosterUrl = t.PosterUrl, BackdropUrl = t.BackdropUrl, Rating = t.ImdbRating,
        ReleaseDate = t.FirstAirDate, OriginCountry = t.OriginCountry, IsPremium = t.IsPremium,
        IsUpcoming = t.FirstAirDate.HasValue && t.FirstAirDate.Value > DateTime.UtcNow,
        Genres = t.TvShowGenres?.Select(g => g.Genre?.Name ?? "").Where(n => n != "").ToList() ?? new(),
        NumberOfSeasons = t.NumberOfSeasons,
        TrailerKey = t.TvShowVideos?.Where(v => v.VideoType == "trailer" && !string.IsNullOrEmpty(v.VideoUrl))
            .Select(v => ExtractYoutubeKey(v.VideoUrl)).FirstOrDefault(k => k != null),
        TrailerVideoUrl = t.TvShowVideos?.Where(v => v.VideoType == "trailer_upload" && !string.IsNullOrEmpty(v.VideoUrl))
            .Select(v => v.VideoUrl).FirstOrDefault()
    };

    public static string? ExtractYoutubeKey(string url)
    {
        var m = Regex.Match(url, @"(?:youtu\.be/|[?&]v=|embed/)([\w-]{11})");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static double DateKey(DateTime? d) => d.HasValue ? (d.Value - DateTime.UnixEpoch).TotalDays : 0;
}
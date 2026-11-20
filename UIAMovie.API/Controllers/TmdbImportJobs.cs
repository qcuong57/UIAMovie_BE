// UIAMovie.API/Controllers/TmdbImportJobs.cs
// Import TMDB chạy nền: POST .../import trả 202 + jobId ngay; FE poll GET /api/admin/tmdb/import-jobs/{jobId}.
// Đăng ký trong Program.cs:  builder.Services.AddSingleton<TmdbImportJobs>();

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces.IServices;
using UIAMovie.Application.Services;
using UIAMovie.Domain.Constants;
using UIAMovie.Infrastructure.Configuration;

namespace UIAMovie.Controllers;

public class TmdbImportJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Kind { get; init; } = "";
    public int TmdbId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string Status { get; set; } = "running";   // running | done | failed
    public string Step { get; set; } = "Đang chờ";
    public object? Result { get; set; }
    public string? Error { get; set; }
    public int ErrorStatus { get; set; }
}

public class TmdbImportJobs
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<Guid, TmdbImportJob> _jobs = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TmdbImportJobs> _logger;

    public TmdbImportJobs(IServiceScopeFactory scopes, ILogger<TmdbImportJobs> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public TmdbImportJob? Get(Guid id) => _jobs.GetValueOrDefault(id);

    /// <summary>Bắt đầu job; nếu (kind, tmdbId) đang chạy thì trả job đó, không chạy trùng.</summary>
    public TmdbImportJob Start(string kind, int tmdbId)
    {
        foreach (var old in _jobs.Values.Where(j => j.Status != "running" && DateTime.UtcNow - j.CreatedAt > Retention))
            _jobs.TryRemove(old.Id, out _);

        var running = _jobs.Values.FirstOrDefault(j => j.Status == "running" && j.Kind == kind && j.TmdbId == tmdbId);
        if (running != null) return running;

        var job = new TmdbImportJob { Kind = kind, TmdbId = tmdbId };
        _jobs[job.Id] = job;
        _ = Task.Run(() => RunAsync(job));
        return job;
    }

    private async Task RunAsync(TmdbImportJob job)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var sp = scope.ServiceProvider;
            job.Result = job.Kind == "movie"
                ? await ImportMovieAsync(sp, job)
                : await ImportTvAsync(sp, job);
            job.Status = "done";
            job.Step = "Hoàn tất";
        }
        catch (KeyNotFoundException ex)
        {
            Fail(job, ex.Message, 404);
        }
        catch (InvalidOperationException ex) when (ex.Data.Contains("conflict"))
        {
            Fail(job, ex.Message, 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import TMDB thất bại ({Kind} {TmdbId})", job.Kind, job.TmdbId);
            Fail(job, "Import thất bại, vui lòng thử lại.", 500);
        }
    }

    private static void Fail(TmdbImportJob job, string message, int status)
    {
        job.Status = "failed";
        job.Error = message;
        job.ErrorStatus = status;
    }

    private static InvalidOperationException Conflict(string message)
    {
        var ex = new InvalidOperationException(message);
        ex.Data["conflict"] = true;
        return ex;
    }

    // ── Phim ──────────────────────────────────────────────────────────────
    private static async Task<object> ImportMovieAsync(IServiceProvider sp, TmdbImportJob job)
    {
        var movies = sp.GetRequiredService<IMovieService>();
        var tmdb = sp.GetRequiredService<ITmdbService>();
        var genres = sp.GetRequiredService<IGenreService>();
        var tmdbId = job.TmdbId;

        var existing = await movies.GetMovieByTmdbIdAsync(tmdbId);
        if (existing != null) throw Conflict($"Phim này đã được import rồi (ID: {existing.Id})");

        job.Step = "Đang lấy dữ liệu từ TMDB";
        var full = await tmdb.GetFullMovieAsync(tmdbId)
                   ?? throw new KeyNotFoundException("Không tìm thấy phim trên TMDB");

        job.Step = "Đang ghi vào cơ sở dữ liệu";
        var genreIds = await genres.ResolveGenreIdsFromTmdbAsync(full.Detail.Genres.Select(g => g.Id));

        var dto = new CreateMovieDTO
        {
            TmdbId = full.Detail.Id,
            Title = full.Detail.Title,
            Description = string.IsNullOrEmpty(full.Detail.Overview) ? full.Detail.Title : full.Detail.Overview,
            ReleaseDate = DateTime.TryParse(full.Detail.ReleaseDate, out var d)
                ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
                : null,
            PosterUrl = full.Detail.PosterUrl,
            BackdropUrl = full.Detail.BackdropUrl,
            Duration = full.Detail.Runtime,
            ImdbRating = (decimal?)full.Detail.VoteAverage,
            OriginCountry = full.Detail.OriginCountry.FirstOrDefault(),
            IsPremium = false, // Import mặc định Free — Admin tự set Premium sau
            GenreIds = genreIds,
            Cast = full.Cast.Take(10).Select(c =>
            {
                full.PersonDetails.TryGetValue(c.Id, out var pd);
                full.PersonImages.TryGetValue(c.Id, out var imgs);
                return new ImportCastDTO
                {
                    TmdbPersonId = c.Id,
                    Name = c.Name,
                    Character = c.Character,
                    Order = c.Order,
                    ProfileUrl = c.ProfileUrl,
                    Biography = pd?.Biography,
                    Birthday = pd?.Birthday,
                    PlaceOfBirth = pd?.PlaceOfBirth,
                    ProfileImages = imgs?.ToList() ?? new()
                };
            }).ToList(),
            Director = full.Director == null ? null : BuildMovieDirector(full),
            Images = full.Backdrops.Select(i => new ImportImageDTO { Url = i.Url!, ImageType = "backdrop" })
                .Concat(full.Posters.Select(i => new ImportImageDTO { Url = i.Url!, ImageType = "poster" }))
                .ToList(),
            Trailers = full.Trailers
                .Select(t => new ImportTrailerDTO { YoutubeUrl = t.YoutubeUrl, Name = t.Name })
                .ToList()
        };

        var movieId = await movies.CreateMovieAsync(dto);
        return new
        {
            movieId,
            genreCount = genreIds.Count,
            castCount = dto.Cast.Count,
            imageCount = dto.Images.Count,
            hasDirector = dto.Director != null,
            personBioCount = full.PersonDetails.Count(kv => !string.IsNullOrEmpty(kv.Value?.Biography)),
            personImageCount = full.PersonImages.Count(kv => kv.Value.Any())
        };
    }

    private static ImportDirectorDTO BuildMovieDirector(TmdbFullMovieDTO full)
    {
        var id = full.Director!.Id;
        full.PersonDetails.TryGetValue(id, out var pd);
        full.PersonImages.TryGetValue(id, out var imgs);
        return new ImportDirectorDTO
        {
            TmdbPersonId = id,
            Name = full.Director.Name,
            ProfileUrl = full.Director.ProfileUrl,
            Biography = pd?.Biography,
            Birthday = pd?.Birthday,
            PlaceOfBirth = pd?.PlaceOfBirth,
            ProfileImages = imgs?.ToList() ?? new()
        };
    }

    // ── TV show ───────────────────────────────────────────────────────────
    private static async Task<object> ImportTvAsync(IServiceProvider sp, TmdbImportJob job)
    {
        var shows = sp.GetRequiredService<ITvShowService>();
        var tmdb = sp.GetRequiredService<ITmdbService>();
        var genres = sp.GetRequiredService<IGenreService>();
        var tmdbId = job.TmdbId;

        var existing = await shows.GetTvShowByTmdbIdAsync(tmdbId);
        if (existing != null) throw Conflict($"TV show này đã được import rồi (ID: {existing.Id})");

        job.Step = "Đang lấy dữ liệu và các season từ TMDB";
        var full = await tmdb.GetFullTvShowAsync(tmdbId)
                   ?? throw new KeyNotFoundException("Không tìm thấy TV show trên TMDB");

        job.Step = "Đang ghi vào cơ sở dữ liệu";
        var genreIds = await genres.ResolveGenreIdsFromTmdbAsync(full.Detail.Genres.Select(g => g.Id));

        var cast = full.Cast.Take(10).Select(c =>
        {
            full.PersonImages.TryGetValue(c.Id, out var imgs);
            full.PersonDetails.TryGetValue(c.Id, out var detail);
            return new ImportCastDTO
            {
                TmdbPersonId = c.Id,
                Name = c.Name,
                Character = c.Character,
                Order = c.Order,
                ProfileUrl = c.ProfileUrl,
                Biography = detail?.Biography,
                Birthday = detail?.Birthday,
                PlaceOfBirth = detail?.PlaceOfBirth,
                ProfileImages = imgs?.ToList() ?? new()
            };
        }).ToList();

        ImportDirectorDTO? director = null;
        if (full.Director != null)
        {
            full.PersonImages.TryGetValue(full.Director.Id, out var imgs);
            full.PersonDetails.TryGetValue(full.Director.Id, out var detail);
            director = new ImportDirectorDTO
            {
                TmdbPersonId = full.Director.Id,
                Name = full.Director.Name,
                ProfileUrl = full.Director.ProfileUrl,
                Biography = detail?.Biography,
                Birthday = detail?.Birthday,
                PlaceOfBirth = detail?.PlaceOfBirth,
                ProfileImages = imgs?.ToList() ?? new()
            };
        }

        var images = full.Backdrops.Take(10)
            .Select(b => new ImportImageDTO { Url = b.Url ?? "", ImageType = "backdrop" })
            .Concat(full.Posters.Take(10).Select(p => new ImportImageDTO { Url = p.Url ?? "", ImageType = "poster" }))
            .Where(i => !string.IsNullOrEmpty(i.Url))
            .ToList();

        var seasons = full.SeasonDetails.Values
            .Where(s => s.SeasonNumber > 0)
            .OrderBy(s => s.SeasonNumber)
            .Select(s => new CreateSeasonDTO
            {
                SeasonNumber = s.SeasonNumber,
                Name = s.Name,
                Overview = string.IsNullOrEmpty(s.Overview) ? null : s.Overview,
                PosterUrl = s.PosterUrl,
                AirDate = DateTime.TryParse(s.AirDate, out var ad) ? ad : null,
                Episodes = s.Episodes
                    .OrderBy(e => e.EpisodeNumber)
                    .Select(e => new CreateEpisodeDTO
                    {
                        EpisodeNumber = e.EpisodeNumber,
                        Title = e.Title,
                        Overview = string.IsNullOrEmpty(e.Overview) ? null : e.Overview,
                        StillUrl = e.StillUrl,
                        Runtime = e.Runtime,
                        Rating = e.VoteAverage > 0 ? (decimal)e.VoteAverage : null,
                        AirDate = DateTime.TryParse(e.AirDate, out var ea) ? ea : null
                    }).ToList()
            }).ToList();

        var dto = new CreateTvShowDTO
        {
            TmdbId = full.Detail.Id,
            Title = full.Detail.Name,
            Description = full.Detail.Overview,
            FirstAirDate = DateTime.TryParse(full.Detail.FirstAirDate, out var fad) ? fad : null,
            LastAirDate = DateTime.TryParse(full.Detail.LastAirDate, out var lad) ? lad : null,
            PosterUrl = full.Detail.PosterUrl,
            BackdropUrl = full.Detail.BackdropUrl,
            EpisodeRuntime = full.Detail.EpisodeRuntime,
            ImdbRating = full.Detail.VoteAverage > 0 ? (decimal)full.Detail.VoteAverage : null,
            OriginCountry = full.Detail.OriginCountry.FirstOrDefault(),
            Status = full.Detail.Status,
            NumberOfSeasons = full.Detail.NumberOfSeasons,
            NumberOfEpisodes = full.Detail.NumberOfEpisodes,
            GenreIds = genreIds,
            Cast = cast,
            Director = director,
            Images = images,
            Trailers = full.Trailers.Select(t => new ImportTrailerDTO { YoutubeUrl = t.YoutubeUrl, Name = t.Name }).ToList(),
            Seasons = seasons
        };

        var showId = await shows.CreateTvShowAsync(dto);
        return new
        {
            showId,
            genreCount = genreIds.Count,
            castCount = cast.Count,
            imageCount = images.Count,
            seasonCount = seasons.Count,
            episodeCount = seasons.Sum(s => s.Episodes.Count),
            hasDirector = director != null,
            personBioCount = full.PersonDetails.Count(kv => !string.IsNullOrEmpty(kv.Value?.Biography)),
            personImageCount = full.PersonImages.Count(kv => kv.Value.Any())
        };
    }
}

[ApiController]
[Route("api/admin/tmdb/import-jobs")]
[Authorize(Roles = Roles.Admin)]
public class TmdbImportJobsController : ControllerBase
{
    private readonly TmdbImportJobs _jobs;
    public TmdbImportJobsController(TmdbImportJobs jobs) => _jobs = jobs;

    [HttpGet("{jobId:guid}")]
    public IActionResult Get(Guid jobId)
    {
        var j = _jobs.Get(jobId);
        if (j == null) return NotFound(new ApiErrorResponseDTO { Message = "Không tìm thấy tiến trình import", StatusCode = 404 });
        return Ok(new ApiResponseDTO<object>
        {
            Data = new { jobId = j.Id, j.Status, j.Step, j.Result, j.Error, j.ErrorStatus },
            Message = "Thành công"
        });
    }
}
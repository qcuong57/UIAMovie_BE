// UIAMovie.Application/Services/AutoRules/BuiltInAutoRules.cs

using System.Text.RegularExpressions;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IRepositories;

namespace UIAMovie.Application.AutoRules;

/// <summary>Đang thịnh hành (điểm trending của repo).</summary>
public class TrendingRule : IAutoRule
{
    private readonly IMovieRepository _movies;
    private readonly ITvShowRepository _tv;
    private readonly IClock _clock;

    public TrendingRule(IMovieRepository movies, ITvShowRepository tv, IClock clock)
    { _movies = movies; _tv = tv; _clock = clock; }

    public string Key => "trending";
    public string Label => "Đang thịnh hành";
    public AutoParamKind ParamKind => AutoParamKind.None;
    public string? ValidateParam(string? param) => null;

    public async Task<List<(HomeContentCardDTO Card, double Key)>> ResolveAsync(AutoRuleContext c)
    {
        var pool = new List<(HomeContentCardDTO Card, double Key)>();
        if (c.WantMovie)
            pool.AddRange((await _movies.GetTrendingAsync(_clock.Today, c.Limit))
                .Where(p => p.Movie.IsPublished).Select(p => (HomeCardMapper.ToCard(p.Movie), (double)p.Score)));
        if (c.WantTv)
            pool.AddRange((await _tv.GetTrendingAsync(_clock.Today, c.Limit))
                .Where(p => p.TvShow.IsPublished).Select(p => (HomeCardMapper.ToCard(p.TvShow), (double)p.Score)));
        return pool;
    }
}

/// <summary>Mới phát hành (sắp theo ngày phát hành giảm dần).</summary>
public class LatestRule : IAutoRule
{
    private readonly IMovieRepository _movies;
    private readonly ITvShowRepository _tv;

    public LatestRule(IMovieRepository movies, ITvShowRepository tv) { _movies = movies; _tv = tv; }

    public string Key => "latest";
    public string Label => "Mới phát hành";
    public AutoParamKind ParamKind => AutoParamKind.None;
    public string? ValidateParam(string? param) => null;

    public async Task<List<(HomeContentCardDTO Card, double Key)>> ResolveAsync(AutoRuleContext c)
    {
        var pool = new List<(HomeContentCardDTO Card, double Key)>();
        if (c.WantMovie)
        {
            var (ms, _) = await _movies.GetPagedAsync(new FilterMoviesDTO
                { SortBy = "releasedate", SortDesc = true, Page = 1, PageSize = c.Limit });
            pool.AddRange(ms.Where(m => m.IsPublished)
                .Select(m => (HomeCardMapper.ToCard(m), HomeCardMapper.DateKey(m.ReleaseDate))));
        }
        if (c.WantTv)
        {
            var (ts, _) = await _tv.GetPagedAsync(new FilterTvShowsDTO
                { SortBy = "firstairdate", SortDesc = true, Page = 1, PageSize = c.Limit });
            pool.AddRange(ts.Where(t => t.IsPublished)
                .Select(t => (HomeCardMapper.ToCard(t), HomeCardMapper.DateKey(t.FirstAirDate))));
        }
        return pool;
    }
}

/// <summary>Base cho các rule sắp theo rating, tuỳ chọn lọc thể loại.</summary>
public abstract class RatingRuleBase : IAutoRule
{
    private readonly IMovieRepository _movies;
    private readonly ITvShowRepository _tv;

    protected RatingRuleBase(IMovieRepository movies, ITvShowRepository tv) { _movies = movies; _tv = tv; }

    public abstract string Key { get; }
    public abstract string Label { get; }
    public abstract AutoParamKind ParamKind { get; }
    public abstract string? ValidateParam(string? param);

    protected virtual List<Guid>? GenreIds(string? param) => null;

    public async Task<List<(HomeContentCardDTO Card, double Key)>> ResolveAsync(AutoRuleContext c)
    {
        var genreIds = GenreIds(c.Param);
        var pool = new List<(HomeContentCardDTO Card, double Key)>();
        if (c.WantMovie)
        {
            var (ms, _) = await _movies.GetPagedAsync(new FilterMoviesDTO
                { SortBy = "rating", SortDesc = true, Page = 1, PageSize = c.Limit, GenreIds = genreIds });
            pool.AddRange(ms.Where(m => m.IsPublished)
                .Select(m => (HomeCardMapper.ToCard(m), (double)(m.ImdbRating ?? 0))));
        }
        if (c.WantTv)
        {
            var (ts, _) = await _tv.GetPagedAsync(new FilterTvShowsDTO
                { SortBy = "rating", SortDesc = true, Page = 1, PageSize = c.Limit, GenreIds = genreIds });
            pool.AddRange(ts.Where(t => t.IsPublished)
                .Select(t => (HomeCardMapper.ToCard(t), (double)(t.ImdbRating ?? 0))));
        }
        return pool;
    }
}

/// <summary>Đánh giá cao (toàn bộ thể loại).</summary>
public class TopRatedRule : RatingRuleBase
{
    public TopRatedRule(IMovieRepository movies, ITvShowRepository tv) : base(movies, tv) { }
    public override string Key => "top_rated";
    public override string Label => "Đánh giá cao";
    public override AutoParamKind ParamKind => AutoParamKind.None;
    public override string? ValidateParam(string? param) => null;
}

/// <summary>Theo thể loại (param = GenreId), sắp theo rating.</summary>
public class GenreRule : RatingRuleBase
{
    public GenreRule(IMovieRepository movies, ITvShowRepository tv) : base(movies, tv) { }
    public override string Key => "genre";
    public override string Label => "Theo thể loại";
    public override AutoParamKind ParamKind => AutoParamKind.Genre;
    public override string? ValidateParam(string? param)
        => Guid.TryParse(param, out _) ? null : "Rule 'genre' cần AutoParam là GenreId hợp lệ.";
    protected override List<Guid>? GenreIds(string? param)
        => Guid.TryParse(param, out var gid) ? new List<Guid> { gid } : null;
}

/// <summary>Sắp chiếu: phim / TV show có ngày phát hành trong tương lai, gần nhất đứng trước.</summary>
public class UpcomingRule : IAutoRule
{
    private readonly IMovieRepository _movies;
    private readonly ITvShowRepository _tv;

    public UpcomingRule(IMovieRepository movies, ITvShowRepository tv) { _movies = movies; _tv = tv; }

    public string Key => "upcoming";
    public string Label => "Sắp chiếu";
    public AutoParamKind ParamKind => AutoParamKind.None;
    public string? ValidateParam(string? param) => null;

    public async Task<List<(HomeContentCardDTO Card, double Key)>> ResolveAsync(AutoRuleContext c)
    {
        var pool = new List<(HomeContentCardDTO Card, double Key)>();
        if (c.WantMovie)
        {
            var (ms, _) = await _movies.GetPagedAsync(new FilterMoviesDTO
                { IsUpcoming = true, SortBy = "releasedate", SortDesc = false, Page = 1, PageSize = c.Limit });
            // Key càng lớn càng đứng trước → đảo dấu để ngày chiếu gần nhất lên đầu
            pool.AddRange(ms.Where(m => m.IsPublished)
                .Select(m => (HomeCardMapper.ToCard(m), -HomeCardMapper.DateKey(m.ReleaseDate))));
        }
        if (c.WantTv)
        {
            var (ts, _) = await _tv.GetPagedAsync(new FilterTvShowsDTO
                { IsUpcoming = true, SortBy = "firstairdate", SortDesc = false, Page = 1, PageSize = c.Limit });
            pool.AddRange(ts.Where(t => t.IsPublished)
                .Select(t => (HomeCardMapper.ToCard(t), -HomeCardMapper.DateKey(t.FirstAirDate))));
        }
        return pool;
    }
}

/// <summary>Base cho rule có tham số, sắp theo rating. Lớp con chỉ cần khai báo cách lọc.</summary>
public abstract class ParamRatingRuleBase : IAutoRule
{
    private readonly IMovieRepository _movies;
    private readonly ITvShowRepository _tv;

    protected ParamRatingRuleBase(IMovieRepository movies, ITvShowRepository tv) { _movies = movies; _tv = tv; }

    public abstract string Key { get; }
    public abstract string Label { get; }
    public abstract AutoParamKind ParamKind { get; }
    public abstract string? ValidateParam(string? param);

    protected abstract void ConfigureMovie(FilterMoviesDTO filter, string param);
    protected abstract void ConfigureTv(FilterTvShowsDTO filter, string param);

    public async Task<List<(HomeContentCardDTO Card, double Key)>> ResolveAsync(AutoRuleContext c)
    {
        var pool = new List<(HomeContentCardDTO Card, double Key)>();
        var param = (c.Param ?? "").Trim();

        if (c.WantMovie)
        {
            var f = new FilterMoviesDTO { SortBy = "rating", SortDesc = true, Page = 1, PageSize = c.Limit };
            ConfigureMovie(f, param);
            var (ms, _) = await _movies.GetPagedAsync(f);
            pool.AddRange(ms.Where(m => m.IsPublished)
                .Select(m => (HomeCardMapper.ToCard(m), (double)(m.ImdbRating ?? 0))));
        }
        if (c.WantTv)
        {
            var f = new FilterTvShowsDTO { SortBy = "rating", SortDesc = true, Page = 1, PageSize = c.Limit };
            ConfigureTv(f, param);
            var (ts, _) = await _tv.GetPagedAsync(f);
            pool.AddRange(ts.Where(t => t.IsPublished)
                .Select(t => (HomeCardMapper.ToCard(t), (double)(t.ImdbRating ?? 0))));
        }
        return pool;
    }
}

/// <summary>Theo năm phát hành (param = năm, VD 2024), sắp theo rating.</summary>
public class YearRule : ParamRatingRuleBase
{
    public YearRule(IMovieRepository movies, ITvShowRepository tv) : base(movies, tv) { }

    public override string Key => "year";
    public override string Label => "Theo năm";
    public override AutoParamKind ParamKind => AutoParamKind.Year;

    public override string? ValidateParam(string? param)
        => int.TryParse(param, out var y) && y is >= 1900 and <= 2100
            ? null
            : "Rule 'year' cần AutoParam là năm hợp lệ (1900–2100).";

    protected override void ConfigureMovie(FilterMoviesDTO f, string param)
    {
        var y = int.Parse(param);
        f.FromReleaseDate = new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        f.ToReleaseDate   = new DateTime(y, 12, 31, 23, 59, 59, DateTimeKind.Utc);
    }

    protected override void ConfigureTv(FilterTvShowsDTO f, string param)
    {
        var y = int.Parse(param);
        f.FromFirstAirDate = new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        f.ToFirstAirDate   = new DateTime(y, 12, 31, 23, 59, 59, DateTimeKind.Utc);
    }
}

/// <summary>Theo quốc gia sản xuất (param = mã ISO 3166-1 alpha-2, VD KR, JP), sắp theo rating.</summary>
public class CountryRule : ParamRatingRuleBase
{
    private static readonly Regex Iso2 = new("^[A-Za-z]{2}$", RegexOptions.Compiled);

    public CountryRule(IMovieRepository movies, ITvShowRepository tv) : base(movies, tv) { }

    public override string Key => "country";
    public override string Label => "Theo quốc gia";
    public override AutoParamKind ParamKind => AutoParamKind.Country;

    public override string? ValidateParam(string? param)
        => param != null && Iso2.IsMatch(param.Trim())
            ? null
            : "Rule 'country' cần AutoParam là mã quốc gia 2 chữ cái (VD: KR, JP, US).";

    protected override void ConfigureMovie(FilterMoviesDTO f, string param) => f.OriginCountry = param.ToUpperInvariant();
    protected override void ConfigureTv(FilterTvShowsDTO f, string param) => f.OriginCountry = param.ToUpperInvariant();
}
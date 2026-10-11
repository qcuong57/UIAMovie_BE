// UIAMovie.Application/Interfaces/IRepositories/TrendingTvShowProjection.cs

using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Interfaces.IRepositories;

/// <summary>Kết quả trending TV show từ repository (song song với TrendingMovieProjection).</summary>
public class TrendingTvShowProjection
{
    public TvShow TvShow { get; set; } = null!;
    public int Views7d { get; set; }
    public int Views30d { get; set; }
    public double Score { get; set; }
}
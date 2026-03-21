namespace UIAMovie.Application.DTOs;

public class FilterMoviesDTO
{
    public List<Guid>? GenreIds { get; set; }
    public decimal? MinRating { get; set; }
    public decimal? MaxRating { get; set; }
    public DateTime? FromReleaseDate { get; set; }
    public DateTime? ToReleaseDate { get; set; }
    public string? Search { get; set; }
    /// <summary>Lọc theo quốc gia sản xuất — ISO 3166-1 alpha-2, VD: "US", "KR", "JP"</summary>
    public string? OriginCountry { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "rating"; // rating, title, releaseDate
    public bool SortDesc { get; set; } = true;
}
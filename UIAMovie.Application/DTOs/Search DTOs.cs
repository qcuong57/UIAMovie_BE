namespace UIAMovie.Application.DTOs;

public class SearchMoviesDTO
{
    public string Query { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

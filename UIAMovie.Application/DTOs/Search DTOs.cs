namespace UIAMovie.Application.DTOs;

public class SearchMoviesDTO
{
    public string Query { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class SearchByActorDTO
{
    public string ActorName { get; set; } = string.Empty;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
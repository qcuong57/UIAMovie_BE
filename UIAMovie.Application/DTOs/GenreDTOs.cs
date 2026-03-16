// UIAMovie.Application/DTOs/GenreDTOs.cs

namespace UIAMovie.Application.DTOs;

public class GenreDTO
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MovieCount { get; set; }
}

public class CreateGenreDTO
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateGenreDTO
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}
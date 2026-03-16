namespace UIAMovie.Application.DTOs;

public class AddFavoriteDTO
{
    public Guid MovieId { get; set; }
}

public class RemoveFavoriteDTO
{
    public Guid MovieId { get; set; }
}

public class FavoriteDTO
{
    public Guid Id { get; set; }
    public Guid MovieId { get; set; }
    public string MovieTitle { get; set; }
    public string? PosterUrl { get; set; }
    public decimal? Rating { get; set; }
    public DateTime AddedAt { get; set; }
}
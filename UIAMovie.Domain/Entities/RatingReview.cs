namespace UIAMovie.Domain.Entities;

public class RatingReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid MovieId { get; set; }
    public int Rating { get; set; }            // 1-10
    public string? ReviewText { get; set; }
    public bool IsSpoiler { get; set; } = false;
    public bool IsPublished { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation Properties
    public User? User { get; set; }
    public Movie? Movie { get; set; }
}
namespace UIAMovie.Domain.Entities;

/// <summary>
/// Ảnh profile của một người (diễn viên / đạo diễn) — lấy từ TMDB /person/{id}/images.
/// </summary>
public class PersonImage
{
    public Guid   Id        { get; set; } = Guid.NewGuid();
    public Guid   PersonId  { get; set; }
    public string Url       { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // FK
    public Person? Person { get; set; }
}
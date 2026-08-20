namespace UIAMovie.Application.DTOs;

public class PersonDTO
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ProfileUrl { get; set; }
    public int? TmdbPersonId { get; set; }
    public string? Biography { get; set; }
    public string? Birthday { get; set; }
    public string? PlaceOfBirth { get; set; }
    public List<string> ProfileImages { get; set; } = new();
}

public class CreatePersonDTO
{
    public string Name { get; set; } = string.Empty;
    public int? TmdbPersonId { get; set; }
    public string? ProfileUrl { get; set; }
    public string? Biography { get; set; }
    public string? Birthday { get; set; }
    public string? PlaceOfBirth { get; set; }
    public List<string>? ProfileImages { get; set; }
}

public class UpdatePersonDTO
{
    public string? Name { get; set; }
    public string? ProfileUrl { get; set; }
    public string? Biography { get; set; }
    public string? Birthday { get; set; }
    public string? PlaceOfBirth { get; set; }
    public List<string>? ProfileImages { get; set; }
}
public class MergePersonsDTO
{
    /// <summary>Person được giữ lại — nhận toàn bộ MovieCast/MovieDirector/PersonImage từ các bản trùng.</summary>
    public Guid PrimaryPersonId { get; set; }
 
    /// <summary>Các Person trùng sẽ bị gộp vào PrimaryPersonId rồi xóa. Id trùng với PrimaryPersonId sẽ bị bỏ qua.</summary>
    public List<Guid> DuplicatePersonIds { get; set; } = new();
}
 
/// <summary>Một nhóm Person nghi trùng tên — trả về từ GET /api/persons/duplicates.</summary>
public class PersonDuplicateGroupDTO
{
    /// <summary>Tên đã chuẩn hóa (Trim + ToLower) dùng để group.</summary>
    public string NormalizedName { get; set; } = string.Empty;
 
    public List<PersonDTO> People { get; set; } = new();
}
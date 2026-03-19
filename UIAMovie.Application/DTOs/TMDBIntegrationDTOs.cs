using System.Text.Json.Serialization;

// ── Existing DTOs (giữ nguyên) ────────────────────────────────────────────────

public class TmdbMovieDTO
{
    [JsonPropertyName("id")]            public int Id { get; set; }
    [JsonPropertyName("title")]         public string Title { get; set; } = string.Empty;
    [JsonPropertyName("overview")]      public string Overview { get; set; } = string.Empty;
    [JsonPropertyName("release_date")]  public string? ReleaseDate { get; set; }
    [JsonPropertyName("poster_path")]   public string? PosterPath { get; set; }
    [JsonPropertyName("backdrop_path")] public string? BackdropPath { get; set; }
    [JsonPropertyName("vote_average")]  public double VoteAverage { get; set; }
    [JsonPropertyName("genre_ids")]     public List<int> GenreIds { get; set; } = new();
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
}

public class TmdbMovieDetailDTO : TmdbMovieDTO
{
    [JsonPropertyName("runtime")]  public int? Runtime { get; set; }
    [JsonPropertyName("imdb_id")] public string? ImdbId { get; set; }
    [JsonPropertyName("genres")]   public List<TmdbGenreDTO> Genres { get; set; } = new();
}

public class TmdbSearchResponseDTO
{
    [JsonPropertyName("results")]       public List<TmdbMovieDTO> Results { get; set; } = new();
    [JsonPropertyName("total_pages")]   public int TotalPages { get; set; }
    [JsonPropertyName("total_results")] public int TotalResults { get; set; }
}

public class TmdbGenreDTO
{
    [JsonPropertyName("id")]   public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

public class TmdbGenreResponseDTO
{
    [JsonPropertyName("genres")] public List<TmdbGenreDTO> Genres { get; set; } = new();
}

public class TmdbVideoItemDTO
{
    [JsonPropertyName("key")]  public string Key { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
}

public class TmdbTrailerDTO
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string YoutubeUrl { get; set; } = string.Empty;
}

public class TmdbVideoResponseDTO
{
    [JsonPropertyName("results")] public List<TmdbVideoItemDTO> Results { get; set; } = new();
}

// ── NEW: Credits (Cast + Crew) ────────────────────────────────────────────────

public class TmdbCastDTO
{
    [JsonPropertyName("id")]           public int Id { get; set; }
    [JsonPropertyName("name")]         public string Name { get; set; } = string.Empty;
    [JsonPropertyName("character")]    public string Character { get; set; } = string.Empty;
    [JsonPropertyName("order")]        public int Order { get; set; }
    [JsonPropertyName("profile_path")] public string? ProfilePath { get; set; }
    public string? ProfileUrl { get; set; }
}

public class TmdbCrewDTO
{
    [JsonPropertyName("id")]           public int Id { get; set; }
    [JsonPropertyName("name")]         public string Name { get; set; } = string.Empty;
    [JsonPropertyName("job")]          public string Job { get; set; } = string.Empty;
    [JsonPropertyName("department")]   public string Department { get; set; } = string.Empty;
    [JsonPropertyName("profile_path")] public string? ProfilePath { get; set; }
    public string? ProfileUrl { get; set; }
}

public class TmdbCreditsResponseDTO
{
    [JsonPropertyName("cast")] public List<TmdbCastDTO> Cast { get; set; } = new();
    [JsonPropertyName("crew")] public List<TmdbCrewDTO> Crew { get; set; } = new();
}

// ── NEW: Images ───────────────────────────────────────────────────────────────

public class TmdbImageDTO
{
    [JsonPropertyName("file_path")]    public string FilePath { get; set; } = string.Empty;
    [JsonPropertyName("vote_average")] public double VoteAverage { get; set; }
    public string? Url { get; set; }
}

public class TmdbImagesResponseDTO
{
    [JsonPropertyName("backdrops")] public List<TmdbImageDTO> Backdrops { get; set; } = new();
    [JsonPropertyName("posters")]   public List<TmdbImageDTO> Posters { get; set; } = new();
}

// ── NEW: Gom tất cả lại cho 1 lần import ─────────────────────────────────────

public class TmdbFullMovieDTO
{
    public TmdbMovieDetailDTO   Detail    { get; set; } = null!;
    public List<TmdbCastDTO>    Cast      { get; set; } = new();
    public TmdbCrewDTO?         Director  { get; set; }
    public List<TmdbImageDTO>   Backdrops { get; set; } = new();
    public List<TmdbImageDTO>   Posters   { get; set; } = new();
    public List<TmdbTrailerDTO> Trailers  { get; set; } = new();
}
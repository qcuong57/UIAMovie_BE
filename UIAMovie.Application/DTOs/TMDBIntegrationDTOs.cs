using System.Text.Json.Serialization;

public class TmdbMovieDTO
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;

    [JsonPropertyName("overview")] public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("release_date")] public string? ReleaseDate { get; set; }

    [JsonPropertyName("poster_path")] public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")] public string? BackdropPath { get; set; }

    [JsonPropertyName("vote_average")] public double VoteAverage { get; set; }

    [JsonPropertyName("genre_ids")] public List<int> GenreIds { get; set; } = new();

    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
}

public class TmdbMovieDetailDTO : TmdbMovieDTO
{
    [JsonPropertyName("runtime")] public int? Runtime { get; set; }

    [JsonPropertyName("imdb_id")] public string? ImdbId { get; set; }

    [JsonPropertyName("genres")] public List<TmdbGenreDTO> Genres { get; set; } = new();
}

public class TmdbSearchResponseDTO
{
    [JsonPropertyName("results")] public List<TmdbMovieDTO> Results { get; set; } = new();

    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }

    [JsonPropertyName("total_results")] public int TotalResults { get; set; }
}

public class TmdbGenreDTO
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

public class TmdbGenreResponseDTO
{
    [JsonPropertyName("genres")] public List<TmdbGenreDTO> Genres { get; set; } = new();
}

public class TmdbVideoItemDTO
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;

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
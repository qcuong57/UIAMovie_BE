using System.Text.Json.Serialization;

// ── Existing DTOs ─────────────────────────────────────────────────────────────

public class TmdbMovieDTO
{
    [JsonPropertyName("id")]            public int    Id          { get; set; }
    [JsonPropertyName("title")]         public string Title       { get; set; } = string.Empty;
    [JsonPropertyName("overview")]      public string Overview    { get; set; } = string.Empty;
    [JsonPropertyName("release_date")]  public string? ReleaseDate { get; set; }
    [JsonPropertyName("poster_path")]   public string? PosterPath  { get; set; }
    [JsonPropertyName("backdrop_path")] public string? BackdropPath { get; set; }
    [JsonPropertyName("vote_average")]  public double VoteAverage  { get; set; }
    [JsonPropertyName("genre_ids")]     public List<int> GenreIds  { get; set; } = new();
    /// <summary>Mã quốc gia sản xuất — ISO 3166-1 alpha-2, VD: ["US"], ["KR"]</summary>
    [JsonPropertyName("origin_country")] public List<string> OriginCountry { get; set; } = new();
    public string? PosterUrl   { get; set; }
    public string? BackdropUrl { get; set; }
}

public class TmdbMovieDetailDTO : TmdbMovieDTO
{
    [JsonPropertyName("runtime")]  public int?    Runtime { get; set; }
    [JsonPropertyName("imdb_id")] public string? ImdbId  { get; set; }
    [JsonPropertyName("genres")]   public List<TmdbGenreDTO> Genres { get; set; } = new();
}

public class TmdbSearchResponseDTO
{
    [JsonPropertyName("results")]       public List<TmdbMovieDTO> Results      { get; set; } = new();
    [JsonPropertyName("total_pages")]   public int                TotalPages   { get; set; }
    [JsonPropertyName("total_results")] public int                TotalResults { get; set; }
}

public class TmdbGenreDTO
{
    [JsonPropertyName("id")]   public int    Id   { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

public class TmdbGenreResponseDTO
{
    [JsonPropertyName("genres")] public List<TmdbGenreDTO> Genres { get; set; } = new();
}

public class TmdbVideoItemDTO
{
    [JsonPropertyName("key")]  public string Key  { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
}

public class TmdbTrailerDTO
{
    public string Key        { get; set; } = string.Empty;
    public string Name       { get; set; } = string.Empty;
    public string Type       { get; set; } = string.Empty;
    public string YoutubeUrl { get; set; } = string.Empty;
}

public class TmdbVideoResponseDTO
{
    [JsonPropertyName("results")] public List<TmdbVideoItemDTO> Results { get; set; } = new();
}

// ── Credits (Cast + Crew) ─────────────────────────────────────────────────────

public class TmdbCastDTO
{
    [JsonPropertyName("id")]           public int     Id          { get; set; }
    [JsonPropertyName("name")]         public string  Name        { get; set; } = string.Empty;
    [JsonPropertyName("character")]    public string  Character   { get; set; } = string.Empty;
    [JsonPropertyName("order")]        public int     Order       { get; set; }
    [JsonPropertyName("profile_path")] public string? ProfilePath { get; set; }
    public string? ProfileUrl { get; set; }
}

public class TmdbCrewDTO
{
    [JsonPropertyName("id")]           public int     Id          { get; set; }
    [JsonPropertyName("name")]         public string  Name        { get; set; } = string.Empty;
    [JsonPropertyName("job")]          public string  Job         { get; set; } = string.Empty;
    [JsonPropertyName("department")]   public string  Department  { get; set; } = string.Empty;
    [JsonPropertyName("profile_path")] public string? ProfilePath { get; set; }
    public string? ProfileUrl { get; set; }
}

public class TmdbCreditsResponseDTO
{
    [JsonPropertyName("cast")] public List<TmdbCastDTO> Cast { get; set; } = new();
    [JsonPropertyName("crew")] public List<TmdbCrewDTO> Crew { get; set; } = new();
}

// ── Images ────────────────────────────────────────────────────────────────────

public class TmdbImageDTO
{
    [JsonPropertyName("file_path")]    public string  FilePath    { get; set; } = string.Empty;
    [JsonPropertyName("vote_average")] public double  VoteAverage { get; set; }
    public string? Url { get; set; }
}

public class TmdbImagesResponseDTO
{
    [JsonPropertyName("backdrops")] public List<TmdbImageDTO> Backdrops { get; set; } = new();
    [JsonPropertyName("posters")]   public List<TmdbImageDTO> Posters   { get; set; } = new();
}

// ── Person Images ─────────────────────────────────────────────────────────────

public class TmdbPersonProfileDTO
{
    [JsonPropertyName("file_path")]    public string FilePath    { get; set; } = string.Empty;
    [JsonPropertyName("vote_average")] public double VoteAverage { get; set; }
}

public class TmdbPersonImagesResponseDTO
{
    [JsonPropertyName("profiles")] public List<TmdbPersonProfileDTO> Profiles { get; set; } = new();
}

// ── Person Detail — tiểu sử diễn viên / đạo diễn ─────────────────────────────

public class TmdbPersonDetailDTO
{
    [JsonPropertyName("id")]                   public int     Id                 { get; set; }
    [JsonPropertyName("name")]                 public string  Name               { get; set; } = string.Empty;
    [JsonPropertyName("biography")]            public string? Biography          { get; set; }
    [JsonPropertyName("birthday")]             public string? Birthday           { get; set; }
    [JsonPropertyName("deathday")]             public string? Deathday           { get; set; }
    [JsonPropertyName("place_of_birth")]       public string? PlaceOfBirth       { get; set; }
    [JsonPropertyName("profile_path")]         public string? ProfilePath        { get; set; }
    [JsonPropertyName("known_for_department")] public string? KnownForDepartment { get; set; }
    [JsonPropertyName("gender")]               public int     Gender             { get; set; }
    [JsonPropertyName("popularity")]           public double  Popularity         { get; set; }
    public string? ProfileUrl { get; set; }
}

// ── Full movie — gom tất cả cho 1 lần import ─────────────────────────────────

public class TmdbFullMovieDTO
{
    public TmdbMovieDetailDTO   Detail    { get; set; } = null!;
    public List<TmdbCastDTO>    Cast      { get; set; } = new();
    public TmdbCrewDTO?         Director  { get; set; }
    public List<TmdbImageDTO>   Backdrops { get; set; } = new();
    public List<TmdbImageDTO>   Posters   { get; set; } = new();
    public List<TmdbTrailerDTO> Trailers  { get; set; } = new();

    /// <summary>
    /// Key = TmdbPersonId → danh sách URL ảnh profile.
    /// Tối đa 5 ảnh / người, sắp xếp theo vote_average giảm dần.
    /// </summary>
    public Dictionary<int, List<string>> PersonImages { get; set; } = new();

    /// <summary>
    /// Key = TmdbPersonId → tiểu sử.
    /// </summary>
    public Dictionary<int, TmdbPersonDetailDTO?> PersonDetails { get; set; } = new();
}

// ════════════════════════════════════════════════════════════════════════════
// TV SHOW DTOs — bắt đầu từ đây
// ════════════════════════════════════════════════════════════════════════════

// ── TV Show base ──────────────────────────────────────────────────────────────

/// <summary>
/// DTO cho 1 item trong kết quả /search/tv và /trending/tv.
/// TMDB dùng "name" thay "title", "first_air_date" thay "release_date" —
/// cả hai được normalize thành Title / ReleaseDate sau khi deserialize
/// (xem TmdbService.NormalizeTvItem).
/// </summary>
public class TmdbTvDTO
{
    [JsonPropertyName("id")]             public int    Id           { get; set; }
    [JsonPropertyName("name")]           public string Name         { get; set; } = string.Empty;
    [JsonPropertyName("overview")]       public string Overview     { get; set; } = string.Empty;
    [JsonPropertyName("first_air_date")] public string? FirstAirDate { get; set; }
    [JsonPropertyName("poster_path")]    public string? PosterPath   { get; set; }
    [JsonPropertyName("backdrop_path")]  public string? BackdropPath { get; set; }
    [JsonPropertyName("vote_average")]   public double  VoteAverage  { get; set; }
    [JsonPropertyName("genre_ids")]      public List<int> GenreIds   { get; set; } = new();
    [JsonPropertyName("origin_country")] public List<string> OriginCountry { get; set; } = new();

    // Được set sau khi deserialize (BuildImageUrl)
    public string? PosterUrl   { get; set; }
    public string? BackdropUrl { get; set; }
}

/// <summary>
/// Response wrapper cho /search/tv và /trending/tv —
/// tách riêng khỏi TmdbSearchResponseDTO (vốn dùng TmdbMovieDTO)
/// để tránh nhầm lẫn khi deserialize.
/// </summary>
public class TmdbTvSearchResponseDTO
{
    [JsonPropertyName("results")]       public List<TmdbTvDTO> Results      { get; set; } = new();
    [JsonPropertyName("total_pages")]   public int             TotalPages   { get; set; }
    [JsonPropertyName("total_results")] public int             TotalResults { get; set; }
}

// ── TV Show detail ────────────────────────────────────────────────────────────

/// <summary>
/// Response của /tv/{id} — kế thừa TmdbTvDTO và bổ sung metadata series.
/// </summary>
public class TmdbTvDetailDTO : TmdbTvDTO
{
    [JsonPropertyName("number_of_seasons")]  public int?   NumberOfSeasons  { get; set; }
    [JsonPropertyName("number_of_episodes")] public int?   NumberOfEpisodes { get; set; }
    /// <summary>"Returning Series" | "Ended" | "Canceled" | "In Production"</summary>
    [JsonPropertyName("status")]             public string? Status          { get; set; }
    [JsonPropertyName("last_air_date")]      public string? LastAirDate     { get; set; }
    /// <summary>
    /// TMDB trả về mảng (vd: [42, 45]) — ta lấy phần tử đầu tiên làm EpisodeRuntime.
    /// </summary>
    [JsonPropertyName("episode_run_time")]   public List<int> EpisodeRunTime { get; set; } = new();
    [JsonPropertyName("genres")]             public List<TmdbGenreDTO>   Genres  { get; set; } = new();
    /// <summary>
    /// Season summary list — đã có sẵn trong detail response, không cần gọi thêm endpoint.
    /// Không bao gồm episode list; cần gọi /tv/{id}/season/{n} riêng.
    /// </summary>
    [JsonPropertyName("seasons")]            public List<TmdbSeasonSummaryDTO> Seasons { get; set; } = new();

    // Computed helper — không map từ JSON
    /// <summary>Lấy runtime đại diện (phần tử đầu tiên của EpisodeRunTime, hoặc null).</summary>
    public int? EpisodeRuntime => EpisodeRunTime.Count > 0 ? EpisodeRunTime[0] : null;
}

// ── Season ────────────────────────────────────────────────────────────────────

/// <summary>
/// Season summary nằm trong TmdbTvDetailDTO.Seasons —
/// chỉ chứa metadata, không có episode list.
/// </summary>
public class TmdbSeasonSummaryDTO
{
    [JsonPropertyName("id")]             public int    Id           { get; set; }
    [JsonPropertyName("season_number")]  public int    SeasonNumber { get; set; }
    [JsonPropertyName("name")]           public string Name         { get; set; } = string.Empty;
    [JsonPropertyName("overview")]       public string Overview     { get; set; } = string.Empty;
    [JsonPropertyName("poster_path")]    public string? PosterPath  { get; set; }
    [JsonPropertyName("air_date")]       public string? AirDate     { get; set; }
    [JsonPropertyName("episode_count")]  public int    EpisodeCount { get; set; }

    // Được set sau khi deserialize
    public string? PosterUrl { get; set; }
}

/// <summary>
/// Response của /tv/{id}/season/{season_number} —
/// chứa đầy đủ episode list của 1 season cụ thể.
/// </summary>
public class TmdbSeasonDetailDTO
{
    [JsonPropertyName("id")]            public int    Id           { get; set; }
    [JsonPropertyName("season_number")] public int    SeasonNumber { get; set; }
    [JsonPropertyName("name")]          public string Name         { get; set; } = string.Empty;
    [JsonPropertyName("overview")]      public string Overview     { get; set; } = string.Empty;
    [JsonPropertyName("poster_path")]   public string? PosterPath  { get; set; }
    [JsonPropertyName("air_date")]      public string? AirDate     { get; set; }
    [JsonPropertyName("episodes")]      public List<TmdbEpisodeDTO> Episodes { get; set; } = new();

    public string? PosterUrl { get; set; }
}

// ── Episode ───────────────────────────────────────────────────────────────────

/// <summary>
/// Episode item trong TmdbSeasonDetailDTO.Episodes.
/// </summary>
public class TmdbEpisodeDTO
{
    [JsonPropertyName("id")]             public int    Id            { get; set; }
    [JsonPropertyName("episode_number")] public int    EpisodeNumber { get; set; }
    [JsonPropertyName("name")]           public string Title         { get; set; } = string.Empty;
    [JsonPropertyName("overview")]       public string Overview      { get; set; } = string.Empty;
    /// <summary>Thumbnail tĩnh của tập — dùng w300 size.</summary>
    [JsonPropertyName("still_path")]     public string? StillPath    { get; set; }
    [JsonPropertyName("runtime")]        public int?   Runtime       { get; set; }
    [JsonPropertyName("vote_average")]   public double VoteAverage   { get; set; }
    [JsonPropertyName("air_date")]       public string? AirDate      { get; set; }

    // Được set sau khi deserialize
    public string? StillUrl { get; set; }
}

// ── TV Credits — /tv/{id}/aggregate_credits ───────────────────────────────────

/// <summary>
/// TMDB TV dùng /aggregate_credits thay /credits để trả về diễn viên chính xác hơn.
/// Cast item có "roles" thay vì "character" trực tiếp.
/// </summary>
public class TmdbTvCastDTO
{
    [JsonPropertyName("id")]           public int                 Id          { get; set; }
    [JsonPropertyName("name")]         public string              Name        { get; set; } = string.Empty;
    [JsonPropertyName("order")]        public int                 Order       { get; set; }
    [JsonPropertyName("profile_path")] public string?             ProfilePath { get; set; }
    [JsonPropertyName("roles")]        public List<TmdbTvRoleDTO> Roles       { get; set; } = new();

    // Computed — lấy character từ role đầu tiên
    public string Character => Roles.FirstOrDefault()?.Character ?? string.Empty;
    public string? ProfileUrl { get; set; }
}

public class TmdbTvRoleDTO
{
    [JsonPropertyName("character")]    public string Character   { get; set; } = string.Empty;
    [JsonPropertyName("episode_count")] public int   EpisodeCount { get; set; }
}

public class TmdbTvCreditsResponseDTO
{
    [JsonPropertyName("cast")] public List<TmdbTvCastDTO> Cast { get; set; } = new();
    [JsonPropertyName("crew")] public List<TmdbCrewDTO>   Crew { get; set; } = new();
}

// ── TV Genre ──────────────────────────────────────────────────────────────────

/// <summary>Response của /genre/tv/list — reuse TmdbGenreResponseDTO được.</summary>
// (không cần DTO mới — TmdbGenreResponseDTO đã đủ)

// ── Full TV Show — gom tất cả cho 1 lần import ───────────────────────────────

/// <summary>
/// Aggregate object được TmdbService.GetFullTvShowAsync() trả về —
/// chứa toàn bộ dữ liệu cần thiết để import 1 TV show vào DB.
///
/// SeasonDetails: Key = SeasonNumber → episode list của season đó.
/// Chỉ import season có SeasonNumber > 0 (bỏ "Specials" — season 0).
/// </summary>
public class TmdbFullTvShowDTO
{
    public TmdbTvDetailDTO                   Detail        { get; set; } = null!;
    public List<TmdbTvCastDTO>               Cast          { get; set; } = new();
    public TmdbCrewDTO?                      Director      { get; set; }
    public List<TmdbImageDTO>                Backdrops     { get; set; } = new();
    public List<TmdbImageDTO>                Posters       { get; set; } = new();
    public List<TmdbTrailerDTO>              Trailers      { get; set; } = new();
    /// <summary>Key = SeasonNumber → full season detail kèm episode list.</summary>
    public Dictionary<int, TmdbSeasonDetailDTO> SeasonDetails { get; set; } = new();
    /// <summary>Key = TmdbPersonId → danh sách URL ảnh profile (tối đa 5).</summary>
    public Dictionary<int, List<string>>         PersonImages  { get; set; } = new();
    /// <summary>Key = TmdbPersonId → tiểu sử (đã dịch sang tiếng Việt nếu có thể).</summary>
    public Dictionary<int, TmdbPersonDetailDTO?> PersonDetails { get; set; } = new();
}
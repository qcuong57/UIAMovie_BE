// UIAMovie.Application/AI/AiCatalog.cs
// Catalog gọn cho chatbot: 1 model chung cho phim lẻ + phim bộ, CSV đánh số (tiết kiệm token, chống AI bịa UUID),
// parser kết quả, gợi ý so sánh không tốn token, nhận diện tên phim trong câu hỏi.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.AI;

// ─── Model ───────────────────────────────────────────────────────────────────

/// <summary>Một phim lẻ hoặc phim bộ ở dạng gọn để đưa vào prompt.</summary>
public sealed record AiCatalogItem
{
    public Guid Id { get; init; }

    /// <summary>"movie" | "tv"</summary>
    public string Kind { get; init; } = "movie";

    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();
    public double? Rating { get; init; }
    public int? Year { get; init; }
    public string? Country { get; init; }

    /// <summary>Phim lẻ: thời lượng phim. Phim bộ: thời lượng mỗi tập.</summary>
    public int? RuntimeMinutes { get; init; }
    public int? Seasons { get; init; }
    public int? Episodes { get; init; }

    /// <summary>TV: "Returning Series" | "Ended" | "Canceled" | "In Production"</summary>
    public string? Status { get; init; }

    public bool IsPremium { get; init; }
    public bool IsUpcoming { get; init; }
    public string Description { get; init; } = string.Empty;
    public int? TrendingRank { get; init; }

    /// <summary>Chỉ có khi item được map từ DTO chi tiết (GetById). List/search trả về null.</summary>
    public string? Director { get; init; }
    public IReadOnlyList<string> Cast { get; init; } = Array.Empty<string>();

    /// <summary>Ngày phát hành (phim lẻ) / phát sóng đầu tiên (phim bộ), dạng dd/MM/yyyy.</summary>
    public string? ReleaseDate { get; init; }

    /// <summary>Link trailer (YouTube). Chỉ có khi map từ DTO chi tiết.</summary>
    public string? TrailerUrl { get; init; }

    /// <summary>Đạo diễn kèm tiểu sử — chỉ có khi map từ DTO chi tiết.</summary>
    public AiPerson? DirectorInfo { get; init; }

    /// <summary>Diễn viên kèm vai diễn + tiểu sử (tối đa 10, theo Order) — chỉ có khi map từ DTO chi tiết.</summary>
    public IReadOnlyList<AiPerson> CastDetail { get; init; } = Array.Empty<AiPerson>();

    /// <summary>Phim bộ: ngày phát sóng gần nhất, dạng dd/MM/yyyy.</summary>
    public string? LastAirDate { get; init; }

    /// <summary>Phim bộ: danh sách mùa (tên, ngày phát sóng, số tập, giới thiệu) — chỉ có khi map từ DTO chi tiết.</summary>
    public IReadOnlyList<AiSeason> SeasonList { get; init; } = Array.Empty<AiSeason>();

    /// <summary>Chấp nhận DateTime / DateTimeOffset / DateOnly / string — không phụ thuộc kiểu trong DTO.</summary>
    internal static string? FormatDate(object? d) => d switch
    {
        null           => null,
        DateTime x     => x.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateTimeOffset x => x.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateOnly x     => x.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        _              => d.ToString(),
    };

    private static string? YoutubeUrl(string? key, string? fallbackUrl)
        => !string.IsNullOrWhiteSpace(key) ? "https://www.youtube.com/watch?v=" + key.Trim()
         : string.IsNullOrWhiteSpace(fallbackUrl) ? null : fallbackUrl.Trim();

    private static AiPerson? DirectorOf(string? name, AiPerson? detail)
        => detail ?? (string.IsNullOrWhiteSpace(name) ? null : new AiPerson { Name = name.Trim() });

    private static double? Rate(decimal? r) => r is > 0 ? (double)r.Value : null;

    public static AiCatalogItem From(MovieDTO m) => new()
    {
        Id             = m.Id,
        Kind           = "movie",
        Title          = m.Title,
        Genres         = m.Genres,
        Rating         = Rate(m.Rating),
        Year           = m.ReleaseDate?.Year,
        Country        = m.OriginCountry,
        RuntimeMinutes = m.Duration,
        IsPremium      = m.IsPremium,
        IsUpcoming     = m.IsUpcoming,
        Description    = m.Description ?? string.Empty,
        TrendingRank   = (m as TrendingMovieDTO)?.TrendingRank,
        Director       = string.IsNullOrWhiteSpace(m.Director) ? m.DirectorDetail?.Name : m.Director,
        Cast           = m.Cast.OrderBy(c => c.Order).Take(6).Select(c => c.Name).ToList(),
        ReleaseDate    = FormatDate(m.ReleaseDate),
        TrailerUrl     = YoutubeUrl(m.TrailerKey, m.Videos?.FirstOrDefault(v => v.VideoType == "trailer")?.VideoUrl),
        DirectorInfo   = DirectorOf(m.Director,
                            m.DirectorDetail is { } dd ? AiPerson.Of(dd.Name, null, dd.Biography, dd.Birthday, dd.PlaceOfBirth) : null),
        CastDetail     = m.Cast.OrderBy(c => c.Order).Take(10)
                            .Select(c => AiPerson.Of(c.Name, c.Character, c.Biography, c.Birthday, c.PlaceOfBirth))
                            .ToList(),
    };

    public static AiCatalogItem From(TvShowSummaryDTO t) => new()
    {
        Id          = t.Id,
        Kind        = "tv",
        Title       = t.Title,
        Genres      = t.Genres,
        Rating      = Rate(t.Rating),
        Year        = t.FirstAirDate?.Year,
        Country     = t.OriginCountry,
        Seasons     = t.NumberOfSeasons,
        Episodes    = t.NumberOfEpisodes,
        Status      = t.Status,
        IsPremium   = t.IsPremium,
        IsUpcoming  = t.IsUpcoming,
        Description = t.Description ?? string.Empty,
    };

    public static AiCatalogItem From(TvShowDTO t) => new()
    {
        Id             = t.Id,
        Kind           = "tv",
        Title          = t.Title,
        Genres         = t.Genres,
        Rating         = Rate(t.Rating),
        Year           = t.FirstAirDate?.Year,
        Country        = t.OriginCountry,
        RuntimeMinutes = t.EpisodeRuntime,
        Seasons        = t.NumberOfSeasons,
        Episodes       = t.NumberOfEpisodes,
        Status         = t.Status,
        IsPremium      = t.IsPremium,
        IsUpcoming     = t.IsUpcoming,
        Description    = t.Description ?? string.Empty,
        Director       = string.IsNullOrWhiteSpace(t.Director) ? t.DirectorDetail?.Name : t.Director,
        Cast           = t.Cast.OrderBy(c => c.Order).Take(6).Select(c => c.Name).ToList(),
        ReleaseDate    = FormatDate(t.FirstAirDate),
        LastAirDate    = FormatDate(t.LastAirDate),
        TrailerUrl     = YoutubeUrl(t.TrailerKey, t.Videos?.FirstOrDefault(v => v.VideoType == "trailer")?.VideoUrl),
        SeasonList     = (t.Seasons ?? []).OrderBy(x => x.SeasonNumber)
                            .Select(x => new AiSeason
                            {
                                Number       = x.SeasonNumber,
                                Name         = x.Name ?? string.Empty,
                                Overview     = x.Overview,
                                AirDate      = FormatDate(x.AirDate),
                                EpisodeCount = x.EpisodeCount,
                            }).ToList(),
        DirectorInfo   = DirectorOf(t.Director,
                            t.DirectorDetail is { } dd ? AiPerson.Of(dd.Name, null, dd.Biography, dd.Birthday, dd.PlaceOfBirth) : null),
        CastDetail     = t.Cast.OrderBy(c => c.Order).Take(10)
                            .Select(c => AiPerson.Of(c.Name, c.Character, c.Biography, c.Birthday, c.PlaceOfBirth))
                            .ToList(),
    };
}

/// <summary>Một mùa của phim bộ.</summary>
public sealed record AiSeason
{
    public int Number { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Overview { get; init; }
    public string? AirDate { get; init; }
    public int? EpisodeCount { get; init; }

    /// <summary>"Mùa 1 – Chạy đi chờ chi (phát sóng 07/04/2019, 15 tập)"</summary>
    public string ShortLabel()
    {
        var extra = new List<string>();
        if (AirDate is not null) extra.Add("phát sóng " + AirDate);
        if (EpisodeCount is { } e) extra.Add(e + " tập");
        var name = string.IsNullOrWhiteSpace(Name) ? string.Empty : " – " + AiText.Sanitize(Name, 60);
        return $"Mùa {Number}{name}" + (extra.Count > 0 ? $" ({string.Join(", ", extra)})" : string.Empty);
    }
}

/// <summary>Diễn viên / đạo diễn kèm vai diễn và tiểu sử ngắn.</summary>
public sealed record AiPerson
{
    public string Name { get; init; } = string.Empty;
    public string? Character { get; init; }
    public string? Biography { get; init; }
    public string? Birthday { get; init; }
    public string? PlaceOfBirth { get; init; }

    public static AiPerson Of(string? name, string? character, string? bio, object? birthday, string? place) => new()
    {
        Name         = name?.Trim() ?? string.Empty,
        Character    = string.IsNullOrWhiteSpace(character) ? null : character.Trim(),
        Biography    = bio,
        Birthday     = AiCatalogItem.FormatDate(birthday),
        PlaceOfBirth = string.IsNullOrWhiteSpace(place) ? null : place.Trim(),
    };

    /// <summary>"Shameik Moore (vai Miles Morales (voice))" — gọn, dùng cho danh sách diễn viên.</summary>
    public string ShortLabel()
    {
        var n = AiText.Sanitize(Name, 40);
        return string.IsNullOrWhiteSpace(Character) ? n : $"{n} (vai {AiText.Sanitize(Character, 40)})";
    }

    /// <summary>Dòng tiểu sử: ngày sinh, nơi sinh, đoạn giới thiệu đã bỏ câu ghi nguồn Wikipedia.</summary>
    public string BioLine(int maxBio = 350)
    {
        var parts = new List<string>();
        if (Birthday is not null) parts.Add("sinh " + Birthday);
        if (PlaceOfBirth is not null) parts.Add("tại " + AiText.Sanitize(PlaceOfBirth, 60));

        var bio = Biography ?? string.Empty;
        var cut = bio.IndexOf("Mô tả ở trên", StringComparison.Ordinal);
        if (cut > 0) bio = bio[..cut];
        bio = AiText.Sanitize(bio, maxBio);
        if (bio.Length > 0) parts.Add(bio);

        return parts.Count == 0 ? "chưa có dữ liệu" : string.Join("; ", parts);
    }
}

/// <summary>Ngữ cảnh người dùng hiện tại — giúp AI cá nhân hóa (tránh gợi ý phim đã xem, biết gói Free/Premium).</summary>
public sealed record AiUserContext
{
    public bool IsLoggedIn { get; init; }
    public bool IsPremium { get; init; }
    public int? DaysRemaining { get; init; }
    public IReadOnlyList<string> RecentWatched { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Watchlist { get; init; } = Array.Empty<string>();

    public static AiUserContext Guest => new() { IsLoggedIn = false };

    public static AiUserContext From(
        SubscriptionStatusDTO? sub,
        IEnumerable<string>? recentWatched = null,
        IEnumerable<string>? watchlist = null) => new()
    {
        IsLoggedIn    = true,
        IsPremium     = sub?.IsPremium ?? false,
        DaysRemaining = sub?.DaysRemaining,
        RecentWatched = (recentWatched ?? []).Take(10).ToList(),
        Watchlist     = (watchlist ?? []).Take(10).ToList(),
    };

    public string ToPromptBlock()
    {
        var sb = new StringBuilder("[NGƯỜI DÙNG]\n");
        sb.Append("Trạng thái: ");
        sb.AppendLine(!IsLoggedIn ? "Khách (chưa đăng nhập)"
            : IsPremium ? (DaysRemaining is { } d ? $"Premium (còn {d} ngày)" : "Premium")
            : "Free");

        if (RecentWatched.Count > 0)
            sb.Append("Đã xem gần đây: ").AppendLine(string.Join("; ", RecentWatched.Select(t => AiText.Sanitize(t, 60))));
        if (Watchlist.Count > 0)
            sb.Append("Watchlist: ").AppendLine(string.Join("; ", Watchlist.Select(t => AiText.Sanitize(t, 60))));

        return sb.ToString().TrimEnd();
    }
}

// ─── CSV đánh số ─────────────────────────────────────────────────────────────

/// <summary>
/// CSV catalog cho Groq. So với AiMovieCsvBuilder cũ:
///   - Cột "#" (1..N) thay cho GUID 36 ký tự → ít token hơn ~70% cho phần ID và AI không thể bịa UUID.
///   - Thêm year / country / length / access (FREE|PREMIUM|UPCOMING|ONGOING|HOT#n) → trả lời được
///     "phim Hàn dưới 2 tiếng", "phim nào miễn phí", "series còn đang chiếu"...
///   - Gộp movie + tv chung 1 format.
/// </summary>
public static class AiCatalogCsvBuilder
{
    public const string Header = "#|type|title|year|genres|country|rating|length|access|desc";

    public static string Build(IReadOnlyList<AiCatalogItem> items, int descLength = 110)
    {
        var sb = new StringBuilder(items.Count * 150 + 64);
        sb.AppendLine(Header);

        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            sb.Append(i + 1).Append('|')
              .Append(it.Kind).Append('|')
              .Append(AiText.Sanitize(it.Title, 80)).Append('|')
              .Append(it.Year?.ToString(CultureInfo.InvariantCulture) ?? "-").Append('|')
              .Append(AiText.Sanitize(string.Join(", ", it.Genres.Select(AiGenres.ToShort)), 60)).Append('|')
              .Append(string.IsNullOrWhiteSpace(it.Country) ? "-" : it.Country).Append('|')
              .Append(it.Rating is { } r ? r.ToString("F1", CultureInfo.InvariantCulture) : "-").Append('|')
              .Append(LengthText(it)).Append('|')
              .Append(AccessText(it)).Append('|')
              .AppendLine(AiText.Sanitize(it.Description, descLength));
        }
        return sb.ToString();
    }

    /// <summary>Khối chi tiết cho 1 phim/series — dùng khi user hỏi về đúng 1 tác phẩm.</summary>
    public static string BuildDetail(AiCatalogItem it, bool peopleDetail = false, bool seasonDetail = false)
    {
        var sb = new StringBuilder();
        sb.Append("Tên: ").Append(AiText.Sanitize(it.Title, 100))
          .Append(it.Kind == "tv" ? " (phim bộ)" : " (phim lẻ)").AppendLine();
        sb.Append("Năm: ").AppendLine(it.Year?.ToString(CultureInfo.InvariantCulture) ?? "Chưa có dữ liệu");
        if (!string.IsNullOrWhiteSpace(it.ReleaseDate))
            sb.Append(it.Kind == "tv" ? "Ngày phát sóng đầu tiên: " : "Ngày phát hành: ").AppendLine(it.ReleaseDate);
        if (it.Kind == "tv" && !string.IsNullOrWhiteSpace(it.LastAirDate))
            sb.Append("Phát sóng gần nhất: ").AppendLine(it.LastAirDate);
        sb.Append("Thể loại: ").AppendLine(it.Genres.Count > 0 ? string.Join(", ", it.Genres.Select(AiGenres.ToShort)) : "Chưa có dữ liệu");
        sb.Append("Quốc gia: ").AppendLine(AiCountries.NameOf(it.Country));
        sb.Append("Điểm: ").AppendLine(it.Rating is { } r ? $"{r.ToString("F1", CultureInfo.InvariantCulture)}/10" : "Chưa có dữ liệu");
        sb.Append(it.Kind == "tv" ? "Quy mô: " : "Thời lượng: ").AppendLine(LengthText(it, verbose: true));
        sb.Append("Truy cập: ").AppendLine(AccessText(it));
        if (!string.IsNullOrWhiteSpace(it.Director)) sb.Append("Đạo diễn: ").AppendLine(AiText.Sanitize(it.Director, 60));

        // Có vai diễn (từ DTO chi tiết) thì dùng bản đầy đủ, không thì rơi về danh sách tên như cũ.
        if (it.CastDetail.Count > 0)
        {
            // Show thực tế: mọi người cùng 1 "vai" ("Thành viên cố định của chương trình") → nêu 1 lần, đỡ tốn token.
            var roles = it.CastDetail.Select(c => c.Character).Distinct().ToList();
            if (it.CastDetail.Count > 1 && roles.Count == 1 && roles[0] is { } sharedRole)
                sb.Append("Diễn viên/thành viên: ").Append(string.Join(", ", it.CastDetail.Select(c => AiText.Sanitize(c.Name, 40))))
                  .Append(" — vai trò chung: ").AppendLine(AiText.Sanitize(sharedRole, 60));
            else
                sb.Append("Diễn viên: ").AppendLine(string.Join(", ", it.CastDetail.Select(c => c.ShortLabel())));
        }
        else if (it.Cast.Count > 0)
            sb.Append("Diễn viên: ").AppendLine(string.Join(", ", it.Cast.Select(c => AiText.Sanitize(c, 40))));

        if (it.Kind == "tv" && it.SeasonList.Count > 0)
        {
            sb.AppendLine("Các mùa: " + string.Join("; ", it.SeasonList.Select(x => x.ShortLabel())));
            // Giới thiệu từng mùa (thành viên, kênh phát sóng...) khá dài → chỉ nạp khi user hỏi về mùa.
            if (seasonDetail)
                foreach (var x in it.SeasonList.Take(8).Where(x => !string.IsNullOrWhiteSpace(x.Overview)))
                    sb.Append("Giới thiệu mùa ").Append(x.Number).Append(": ").AppendLine(AiText.Sanitize(x.Overview, 260));
        }

        if (!string.IsNullOrWhiteSpace(it.TrailerUrl)) sb.Append("Trailer: ").AppendLine(it.TrailerUrl);
        sb.AppendLine("Nội dung: " + AiText.Sanitize(it.Description, 500));

        // Tiểu sử khá dài (~100 token/người) → chỉ nạp khi user thật sự hỏi về người.
        if (peopleDetail)
        {
            if (it.DirectorInfo is { } d)
                sb.Append("Tiểu sử đạo diễn ").Append(AiText.Sanitize(d.Name, 60)).Append(": ").AppendLine(d.BioLine());
            foreach (var c in it.CastDetail.Take(6))
                sb.Append("Tiểu sử ").Append(AiText.Sanitize(c.Name, 60)).Append(": ").AppendLine(c.BioLine(280));
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Map các số # AI trả về → item thật (bỏ số ngoài phạm vi).</summary>
    public static List<AiCatalogItem> Resolve(IReadOnlyList<AiCatalogItem> items, IEnumerable<int> indices)
        => indices.Where(i => i >= 1 && i <= items.Count).Select(i => items[i - 1]).ToList();

    private static string LengthText(AiCatalogItem it, bool verbose = false)
    {
        if (it.Kind == "tv")
        {
            var parts = new List<string>();
            if (it.Seasons is { } s) parts.Add(verbose ? $"{s} mùa" : $"{s}s");
            if (it.Episodes is { } e) parts.Add(verbose ? $"{e} tập" : $"{e}ep");
            if (it.RuntimeMinutes is { } m) parts.Add(verbose ? $"~{m} phút/tập" : $"{m}m/ep");
            return parts.Count == 0 ? (verbose ? "Chưa có dữ liệu" : "-") : string.Join(verbose ? ", " : "/", parts);
        }
        return it.RuntimeMinutes is { } d ? (verbose ? $"{d} phút" : $"{d}m") : (verbose ? "Chưa có dữ liệu" : "-");
    }

    private static string AccessText(AiCatalogItem it)
    {
        var flags = new List<string> { it.IsPremium ? "PREMIUM" : "FREE" };
        if (it.IsUpcoming) flags.Add("UPCOMING");
        if (it.Kind == "tv" && !string.IsNullOrWhiteSpace(it.Status))
        {
            flags.Add(it.Status switch
            {
                "Ended"                          => "ENDED",
                "Canceled"                       => "CANCELED",
                "Returning Series" or "In Production" => "ONGOING",
                _                                => AiText.Sanitize(it.Status, 20),
            });
        }
        if (it.TrendingRank is { } rank) flags.Add($"HOT#{rank}");
        return string.Join(",", flags);
    }
}

/// <summary>Đọc output của các prompt "Pick" (danh sách số #), an toàn với mọi kiểu AI trả về.</summary>
public static class AiIndexParser
{
    private static readonly Regex IntRegex = new(@"\d+", RegexOptions.Compiled);

    /// <summary>
    /// Chấp nhận <c>[3,1,7]</c>, <c>{"ids":[3,1,7]}</c>, <c>```json ...```</c>, <c>["3","1"]</c>.
    /// Loại số trùng / ngoài [1..itemCount], giữ thứ tự AI xếp hạng.
    /// </summary>
    public static List<int> Parse(string? raw, int itemCount, int take)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(raw) || itemCount <= 0 || take <= 0) return result;

        var start = raw.IndexOf('[');
        var end   = raw.LastIndexOf(']');
        if (start < 0 || end <= start) return result;

        foreach (Match m in IntRegex.Matches(raw.Substring(start, end - start + 1)))
        {
            if (!int.TryParse(m.Value, out var n)) continue;
            if (n < 1 || n > itemCount || result.Contains(n)) continue;
            result.Add(n);
            if (result.Count >= take) break;
        }
        return result;
    }
}

// ─── Gợi ý so sánh (0 token) ─────────────────────────────────────────────────

public static class AiCompareSuggester
{
    /// <summary>Chọn tối đa <paramref name="take"/> tác phẩm đáng đem ra so sánh với <paramref name="anchor"/>.</summary>
    public static IReadOnlyList<AiCatalogItem> SuggestFor(AiCatalogItem anchor, IEnumerable<AiCatalogItem> pool, int take = 3)
    {
        var a = AiGenres.CanonicalSet(anchor.Genres);

        return pool
            .Where(p => p.Id != anchor.Id && !p.IsUpcoming)
            .Select(p => (Item: p, Score: Score(anchor, a, p)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.Rating ?? 0)
            .Take(take)
            .Select(x => x.Item)
            .ToList();
    }

    /// <summary>Câu hỏi sẵn để FE hiện thành chip: "So sánh A và B".</summary>
    public static IReadOnlyList<string> ToChips(AiCatalogItem anchor, IEnumerable<AiCatalogItem> suggestions)
        => suggestions.Select(s => $"So sánh {anchor.Title} và {s.Title}").ToList();

    private static double Score(AiCatalogItem anchor, HashSet<string> anchorGenres, AiCatalogItem p)
    {
        var g = AiGenres.CanonicalSet(p.Genres);
        var inter = g.Count(anchorGenres.Contains);
        if (inter == 0) return 0; // phải có ít nhất 1 thể loại chung mới đáng so sánh

        var union = anchorGenres.Count + g.Count - inter;
        var score = 3.0 * inter / Math.Max(1, union);
        if (p.Kind == anchor.Kind) score += 1;
        if (!string.IsNullOrEmpty(anchor.Country) && string.Equals(anchor.Country, p.Country, StringComparison.OrdinalIgnoreCase)) score += 0.5;
        if (anchor.Rating is { } ra && p.Rating is { } rp) score += 1 - Math.Min(1, Math.Abs(ra - rp) / 3);
        if (anchor.Year is { } ya && p.Year is { } yp) score += Math.Max(0, 0.5 * (1 - Math.Abs(ya - yp) / 10.0));
        return score;
    }
}

// ─── Nhận diện tên phim trong câu hỏi ────────────────────────────────────────

/// <summary>
/// Tìm các tựa phim/series được nhắc trong câu hỏi ("so sánh Inception và Interstellar").
/// Không phân biệt hoa thường/dấu/dấu câu. Tạo 1 lần (singleton) rồi dùng lại cho mọi request.
/// Nhờ đó compare/detail không phải nhờ AI đoán tên phim, và AI chỉ nhận đúng dữ liệu của phim đó.
/// </summary>
public sealed class AiTitleIndex
{
    private const int MinSlugLength = 4; // bỏ tựa quá ngắn ("Up", "It", "Her") để tránh khớp nhầm

    private readonly (AiCatalogItem Item, string Slug)[] _entries;

    public AiTitleIndex(IEnumerable<AiCatalogItem> catalog)
    {
        _entries = catalog
            .Select(i => (Item: i, Slug: AiText.Slug(i.Title)))
            .Where(e => e.Slug.Length >= MinSlugLength)
            .OrderByDescending(e => e.Slug.Length) // tựa dài trước: "Spider-Man 2" thắng "Spider-Man"
            .ToArray();
    }

    /// <returns>Tối đa <paramref name="max"/> tác phẩm, theo thứ tự xuất hiện trong câu.</returns>
    public IReadOnlyList<AiCatalogItem> FindMentioned(string? message, int max = 3)
    {
        var text = " " + AiText.Slug(message) + " ";
        var found = new List<(int Pos, AiCatalogItem Item)>();

        foreach (var (item, slug) in _entries)
        {
            var pos = text.IndexOf(" " + slug + " ", StringComparison.Ordinal);
            if (pos < 0) continue;

            found.Add((pos, item));
            // che phần đã khớp để tựa ngắn nằm bên trong nó không khớp lần nữa
            text = text[..(pos + 1)] + new string('#', slug.Length) + text[(pos + 1 + slug.Length)..];
            if (found.Count >= max) break;
        }

        return found.OrderBy(f => f.Pos).Select(f => f.Item).ToList();
    }
}

// ─── Nhận diện câu hỏi về con người ──────────────────────────────────────────

/// <summary>Quyết định có nạp tiểu sử diễn viên/đạo diễn vào prompt hay không (tiết kiệm token).</summary>
public static class AiDetailFocus
{
    private static readonly AiKeywordSet PeopleKeys = new([
        "tiểu sử", "sinh năm", "sinh ngày", "ngày sinh", "năm sinh", "nơi sinh", "sinh ra", "sinh nhật", "quê", "tuổi",
        "là ai", "ai là", "người mỹ", "quốc tịch", "sự nghiệp", "giải thưởng",
        "biography", "bio", "born", "birthday", "birthplace", "who is", "who plays", "age",
    ]);

    private static readonly AiKeywordSet SeasonKeys = new([
        "mùa", "season", "seasons", "mấy mùa", "phần 1", "phần 2", "phần 3", "phần 4", "phần 5",
        "thành viên", "dàn cast", "kênh nào", "phát sóng", "lên sóng", "bao nhiêu tập", "mấy tập",
    ]);

    /// <summary>Câu hỏi về mùa / thành viên từng mùa / lịch phát sóng của phim bộ.</summary>
    public static bool WantsSeasonDetail(string? message)
    {
        var lower = AiText.Lower(message);
        return SeasonKeys.Any(lower, AiText.RemoveDiacritics(lower));
    }

    public static bool WantsPeopleDetail(string? message)
    {
        var lower = AiText.Lower(message);
        return PeopleKeys.Any(lower, AiText.RemoveDiacritics(lower));
    }
}
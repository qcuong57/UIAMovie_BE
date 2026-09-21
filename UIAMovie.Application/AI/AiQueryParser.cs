// UIAMovie.Application/AI/AiQueryParser.cs
// Rút bộ lọc có cấu trúc từ câu hỏi tự nhiên (thể loại, quốc gia, năm, thời lượng, điểm, miễn phí, sắp chiếu, trending...)
// → lọc catalog TRƯỚC khi gọi AI: prompt nhỏ hơn, kết quả đúng hơn, không "gợi ý đại".
//
// Chỉ nên gọi cho intent duyệt danh sách (movie / tvshow / mood). Với compare/detail hãy dùng AiTitleIndex
// (tựa phim có chứa từ như "Action", "War" sẽ làm parser hiểu nhầm thành thể loại).

using System.Globalization;
using System.Text.RegularExpressions;

namespace UIAMovie.Application.AI;

public sealed record AiQueryHints
{
    /// <summary>"movie" | "tv" | "any"</summary>
    public string Kind { get; init; } = "any";

    /// <summary>Tên thể loại chuẩn (tiếng Anh) — so khớp qua AiGenres.</summary>
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

    public string? CountryCode { get; init; }
    public int? FromYear { get; init; }
    public int? ToYear { get; init; }
    public int? MaxRuntimeMinutes { get; init; }
    public double? MinRating { get; init; }
    public bool FreeOnly { get; init; }
    public bool UpcomingOnly { get; init; }
    public bool Trending { get; init; }
    public bool Newest { get; init; }

    /// <summary>Đoạn text sau "giống/tương tự..." — đưa cho AiTitleIndex để tìm phim gốc.</summary>
    public string? SimilarTo { get; init; }

    public bool HasFilters =>
        Kind != "any" || Genres.Count > 0 || CountryCode is not null || FromYear is not null || ToYear is not null
        || MaxRuntimeMinutes is not null || MinRating is not null || FreeOnly || UpcomingOnly || Trending || Newest;

    /// <summary>Mô tả bộ lọc bằng tiếng Việt để đưa vào prompt ([BỘ LỌC]).</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Kind == "movie") parts.Add("chỉ phim lẻ");
        if (Kind == "tv") parts.Add("chỉ phim bộ/series");
        if (Genres.Count > 0) parts.Add("thể loại: " + string.Join(", ", Genres));
        if (CountryCode is not null) parts.Add("quốc gia: " + AiCountries.NameOf(CountryCode));
        if (FromYear is not null || ToYear is not null)
            parts.Add(FromYear == ToYear ? $"năm {FromYear}" : $"năm {FromYear?.ToString() ?? "..."}–{ToYear?.ToString() ?? "nay"}");
        if (MaxRuntimeMinutes is { } m) parts.Add($"thời lượng ≤ {m} phút");
        if (MinRating is { } r) parts.Add($"điểm ≥ {r.ToString("0.#", CultureInfo.InvariantCulture)}");
        if (FreeOnly) parts.Add("chỉ nội dung miễn phí");
        if (UpcomingOnly) parts.Add("sắp chiếu");
        if (Trending) parts.Add("đang thịnh hành");
        if (Newest) parts.Add("mới nhất");
        return string.Join("; ", parts);
    }

    /// <summary>Lọc + xếp hạng catalog trong bộ nhớ. Mặc định loại phim chưa ra mắt trừ khi hỏi "sắp chiếu".</summary>
    public IReadOnlyList<AiCatalogItem> Apply(IEnumerable<AiCatalogItem> catalog, int take = 25)
    {
        IEnumerable<AiCatalogItem> q = catalog;

        if (Kind != "any") q = q.Where(i => i.Kind == Kind);
        q = UpcomingOnly ? q.Where(i => i.IsUpcoming) : q.Where(i => !i.IsUpcoming);
        if (FreeOnly) q = q.Where(i => !i.IsPremium);
        if (CountryCode is not null) q = q.Where(i => string.Equals(i.Country, CountryCode, StringComparison.OrdinalIgnoreCase));
        if (Genres.Count > 0) q = q.Where(i => AiGenres.MatchesAny(i.Genres, Genres));
        if (FromYear is { } fy) q = q.Where(i => i.Year is { } y && y >= fy);
        if (ToYear is { } ty) q = q.Where(i => i.Year is { } y && y <= ty);
        if (MaxRuntimeMinutes is { } mr) q = q.Where(i => i.RuntimeMinutes is { } r && r <= mr);
        if (MinRating is { } mn) q = q.Where(i => i.Rating is { } r && r >= mn);

        IOrderedEnumerable<AiCatalogItem> ordered;
        if (Trending)
            ordered = q.OrderBy(i => i.TrendingRank ?? int.MaxValue).ThenByDescending(i => i.Rating ?? 0);
        else if (Newest)
            ordered = q.OrderByDescending(i => i.Year ?? 0).ThenByDescending(i => i.Rating ?? 0);
        else
            ordered = q.OrderByDescending(i => Genres.Count > 0 ? AiGenres.MatchCount(i.Genres, Genres) : 0)
                       .ThenByDescending(i => i.Rating ?? 0);

        return ordered.Take(take).ToList();
    }

    /// <summary>
    /// Như Apply nhưng nếu quá ít kết quả thì nới lỏng dần: điểm → thời lượng → năm → quốc gia.
    /// Danh sách đã nới được trả về để AI nói thẳng với user thay vì im lặng gợi ý sai tiêu chí.
    /// </summary>
    public AiFilteredCatalog ApplyWithFallback(IReadOnlyList<AiCatalogItem> catalog, int minResults = 5, int take = 25)
    {
        var stages = new (string? Relaxed, AiQueryHints Hints)[]
        {
            (null, this),
            ("điểm số",       this with { MinRating = null }),
            ("thời lượng",    this with { MinRating = null, MaxRuntimeMinutes = null }),
            ("năm phát hành", this with { MinRating = null, MaxRuntimeMinutes = null, FromYear = null, ToYear = null }),
            ("quốc gia",      this with { MinRating = null, MaxRuntimeMinutes = null, FromYear = null, ToYear = null, CountryCode = null }),
        };

        var relaxed = new List<string>();
        IReadOnlyList<AiCatalogItem> items = Array.Empty<AiCatalogItem>();
        var applied = this;

        foreach (var (name, hints) in stages)
        {
            // bỏ qua stage không thay đổi gì so với stage trước
            if (name is not null && hints.Equals(applied)) continue;

            if (name is not null) relaxed.Add(name);
            applied = hints;
            items = hints.Apply(catalog, take);
            if (items.Count >= minResults) break;
        }

        return new AiFilteredCatalog(items, applied, relaxed);
    }
}

/// <summary>Kết quả lọc catalog kèm thông tin bộ lọc thực tế đã dùng.</summary>
public sealed record AiFilteredCatalog(IReadOnlyList<AiCatalogItem> Items, AiQueryHints Applied, IReadOnlyList<string> Relaxed)
{
    /// <summary>Khối [BỘ LỌC] cho prompt; rỗng nếu user không đặt tiêu chí nào.</summary>
    public string ToPromptBlock(AiQueryHints original)
    {
        if (!original.HasFilters) return string.Empty;
        var text = "[BỘ LỌC]\nNgười dùng yêu cầu: " + original.Describe() + ".";
        if (Relaxed.Count > 0)
            text += "\nKhông đủ kết quả nên đã nới lỏng: " + string.Join(", ", Relaxed) + ". Hãy nói rõ điều này với người dùng.";
        if (Items.Count == 0)
            text += "\nKhông có phim nào phù hợp trong hệ thống.";
        return text;
    }
}

public static class AiQueryParser
{
    // ── Thể loại ──────────────────────────────────────────────────────────────
    private static readonly (string Genre, AiKeywordSet Keys)[] GenreKeys =
    [
        ("Action",          new(["hành động", "action"])),
        ("Adventure",       new(["phiêu lưu", "adventure"])),
        ("Animation",       new(["hoạt hình", "animation", "cartoon"])),
        ("Comedy",          new(["hài", "hài hước", "comedy", "gây cười"])),
        ("Crime",           new(["hình sự", "tội phạm", "crime"])),
        ("Documentary",     new(["tài liệu", "documentary"])),
        ("Drama",           new(["chính kịch", "tâm lý", "drama"])),
        ("Family",          new(["gia đình", "family"])),
        ("Fantasy",         new(["giả tưởng", "fantasy", "thần thoại"])),
        ("History",         new(["lịch sử", "cổ trang", "history"])),
        ("Horror",          new(["kinh dị", "horror", "rùng rợn", "phim ma"])),
        ("Music",           new(["âm nhạc", "musical"])),
        ("Mystery",         new(["bí ẩn", "trinh thám", "mystery"])),
        ("Romance",         new(["lãng mạn", "tình cảm", "romance", "ngôn tình"])),
        ("Science Fiction", new(["khoa học viễn tưởng", "viễn tưởng", "sci-fi", "sci fi", "scifi"])),
        ("Thriller",        new(["gây cấn", "giật gân", "thriller", "ly kỳ"])),
        ("War",             new(["chiến tranh", "war"])),
        ("Western",         new(["miền tây", "cowboy", "western"])),
    ];

    // ── Quốc gia ──────────────────────────────────────────────────────────────
    private static readonly (string Code, AiKeywordSet Keys)[] CountryKeys =
    [
        ("KR", new(["phim hàn", "hàn quốc", "korea", "korean", "k-drama", "kdrama"])),
        ("JP", new(["phim nhật", "nhật bản", "japan", "japanese", "j-drama", "jdrama", "anime"])),
        ("CN", new(["phim trung", "trung quốc", "hoa ngữ", "c-drama", "cdrama"])),
        ("US", new(["phim mỹ", "hollywood", "âu mỹ"])),
        ("TH", new(["phim thái", "thái lan", "t-drama"])),
        ("VN", new(["phim việt", "việt nam"])),
        ("IN", new(["phim ấn", "ấn độ", "bollywood"])),
        ("FR", new(["phim pháp", "nước pháp"])),
        ("GB", new(["vương quốc anh", "phim anh quốc", "british", "phim uk"])),
        ("HK", new(["hồng kông", "hong kong"])),
        ("TW", new(["đài loan", "taiwan"])),
    ];

    // ── Loại nội dung ─────────────────────────────────────────────────────────
    private static readonly AiKeywordSet TvKind = new([
        "phim bộ", "series", "tv show", "tvshow", "phim dài tập", "nhiều tập", "season", "episode",
        "k-drama", "kdrama", "c-drama", "cdrama", "j-drama", "jdrama", "t-drama", "sitcom", "miniseries",
    ]);
    private static readonly AiKeywordSet MovieKind = new([
        "phim lẻ", "chiếu rạp", "phim điện ảnh", "cinema", "movie", "movies", "film",
    ]);

    // ── Cờ trạng thái ─────────────────────────────────────────────────────────
    private static readonly AiKeywordSet FreeKeys = new(["miễn phí", "free", "không cần premium", "không premium", "ko cần premium"]);
    private static readonly AiKeywordSet UpcomingKeys = new(["sắp chiếu", "sắp ra mắt", "sắp phát hành", "sắp lên sóng", "upcoming", "coming soon"]);
    private static readonly AiKeywordSet TrendingKeys = new(["trending", "xu hướng", "đang hot", "thịnh hành", "nhiều người xem", "xem nhiều", "hot nhất"]);
    private static readonly AiKeywordSet NewestKeys = new(["mới nhất", "phim mới", "mới ra", "mới phát hành", "mới chiếu", "latest", "newest"]);
    private static readonly AiKeywordSet HighRatingKeys = new(["điểm cao", "đánh giá cao", "rating cao", "hay nhất", "đáng xem nhất"]);

    // ── Regex (chạy trên text đã bỏ dấu) ──────────────────────────────────────
    private static readonly Regex YearRx = new(@"\b(19\d{2}|20\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex YearFromRx = new(@"\b(?:sau|tu|tren)\s+(?:nam\s+)?(19\d{2}|20\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex YearToRx = new(@"\b(?:truoc|den|toi)\s+(?:nam\s+)?(19\d{2}|20\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex DecadeRx = new(@"\b(?:thap nien|nhung nam|doi|phim)\s*(\d0)s?\b(?!\s*(?:phut|tieng|gio|p\b|h\b))", RegexOptions.Compiled);
    private static readonly Regex DecadeSRx = new(@"\b(\d0)s\b", RegexOptions.Compiled);
    private static readonly Regex RuntimeRx = new(@"\b(?:duoi|khong qua|toi da|ngan hon)\s*(\d+(?:[.,]\d+)?)\s*(phut|p|tieng|gio|h)\b", RegexOptions.Compiled);
    private static readonly Regex ShortFilmRx = new(@"\bphim ngan\b", RegexOptions.Compiled);
    private static readonly Regex RatingRx1 = new(@"(?:\btren|\bhon|\bit nhat|\btu|>=|>)\s*(\d+(?:[.,]\d)?)\s*(?:diem|sao)\b", RegexOptions.Compiled);
    private static readonly Regex RatingRx2 = new(@"\b(?:diem|imdb|rating)\s*(?:>=|>|tren|tu)\s*(\d+(?:[.,]\d)?)\b", RegexOptions.Compiled);
    private static readonly Regex SimilarRx = new(
        @"(?:similar to|movies like|films like|shows like|giong voi|giong nhu|giong|tuong tu|na na|kieu)\s+(?<t>.+?)(?=$|[?.!,;]|\s(?:nhung|ma|va|hon|de)\b)",
        RegexOptions.Compiled);

    public static AiQueryHints Parse(string? message)
    {
        var lower  = AiText.Lower(message);
        var folded = AiText.RemoveDiacritics(lower);

        // Loại nội dung
        var wantsTv    = TvKind.Any(lower, folded);
        var wantsMovie = MovieKind.Any(lower, folded);
        var kind = wantsTv && !wantsMovie ? "tv" : wantsMovie && !wantsTv ? "movie" : "any";

        // Thể loại
        var genres = GenreKeys.Where(g => g.Keys.Any(lower, folded)).Select(g => g.Genre).ToList();

        // Quốc gia (khớp đầu tiên theo thứ tự ưu tiên)
        var country = CountryKeys.FirstOrDefault(c => c.Keys.Any(lower, folded)).Code;

        var (fromYear, toYear) = ParseYears(folded);
        var maxRuntime = ParseMaxRuntime(folded);
        var minRating  = ParseMinRating(folded) ?? (HighRatingKeys.Any(lower, folded) ? 7.5 : null);

        return new AiQueryHints
        {
            Kind              = kind,
            Genres            = genres,
            CountryCode       = country,
            FromYear          = fromYear,
            ToYear            = toYear,
            MaxRuntimeMinutes = maxRuntime,
            MinRating         = minRating,
            FreeOnly          = FreeKeys.Any(lower, folded),
            UpcomingOnly      = UpcomingKeys.Any(lower, folded),
            Trending          = TrendingKeys.Any(lower, folded),
            Newest            = NewestKeys.Any(lower, folded),
            SimilarTo         = ParseSimilarTo(folded),
        };
    }

    private static (int? From, int? To) ParseYears(string folded)
    {
        var fromM = YearFromRx.Match(folded);
        var toM   = YearToRx.Match(folded);
        if (fromM.Success || toM.Success)
            return (fromM.Success ? int.Parse(fromM.Groups[1].Value, CultureInfo.InvariantCulture) : null,
                    toM.Success   ? int.Parse(toM.Groups[1].Value,   CultureInfo.InvariantCulture) : null);

        var years = YearRx.Matches(folded).Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        if (years.Count > 0) return (years.Min(), years.Max());

        var dm = DecadeRx.Match(folded);
        if (!dm.Success) dm = DecadeSRx.Match(folded);
        if (dm.Success)
        {
            var d = int.Parse(dm.Groups[1].Value, CultureInfo.InvariantCulture); // 90 → 1990, 10 → 2010
            var start = d >= 30 ? 1900 + d : 2000 + d;
            return (start, start + 9);
        }
        return (null, null);
    }

    private static int? ParseMaxRuntime(string folded)
    {
        var m = RuntimeRx.Match(folded);
        if (m.Success &&
            double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            var minutes = m.Groups[2].Value is "tieng" or "gio" or "h" ? v * 60 : v;
            return (int)Math.Round(minutes);
        }
        return ShortFilmRx.IsMatch(folded) ? 100 : null;
    }

    private static double? ParseMinRating(string folded)
    {
        var m = RatingRx1.Match(folded);
        if (!m.Success) m = RatingRx2.Match(folded);
        if (m.Success &&
            double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) &&
            v is > 0 and <= 10)
            return v;
        return null;
    }

    private static string? ParseSimilarTo(string folded)
    {
        var m = SimilarRx.Match(folded);
        if (!m.Success) return null;

        var t = m.Groups["t"].Value.Trim();
        foreach (var prefix in new[] { "bo phim ", "phim ", "series " })
            if (t.StartsWith(prefix, StringComparison.Ordinal)) { t = t[prefix.Length..]; break; }

        t = t.Trim();
        return t.Length is >= 3 and <= 60 ? t : null;
    }
}
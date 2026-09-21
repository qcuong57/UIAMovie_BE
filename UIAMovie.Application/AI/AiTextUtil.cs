// UIAMovie.Application/AI/AiTextUtil.cs
// Helper dùng chung cho tầng AI: chuẩn hóa tiếng Việt, match keyword, thể loại, quốc gia.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UIAMovie.Application.AI;

public static class AiText
{
    /// <summary>Chuẩn hóa Unicode (FormC) + lowercase. Giữ nguyên dấu tiếng Việt.</summary>
    public static string Lower(string? s)
        => string.IsNullOrEmpty(s) ? string.Empty : s.Normalize(NormalizationForm.FormC).ToLowerInvariant();

    /// <summary>Bỏ dấu tiếng Việt (đ → d). "Người Nhện" → "Nguoi Nhen".</summary>
    public static string RemoveDiacritics(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var formD = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch { 'đ' => 'd', 'Đ' => 'D', _ => c });
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>lowercase + bỏ dấu + mọi ký tự không phải chữ/số → 1 khoảng trắng. Dùng để so khớp tên phim.</summary>
    public static string Slug(string? s)
        => Regex.Replace(RemoveDiacritics(Lower(s)), @"[^a-z0-9]+", " ").Trim();

    /// <summary>Tìm nguyên từ (không khớp giữa chừng): "vs" không khớp "avsx".</summary>
    public static bool ContainsWord(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return false;
        var from = 0;
        while (true)
        {
            var i = haystack.IndexOf(needle, from, StringComparison.Ordinal);
            if (i < 0) return false;
            var end = i + needle.Length;
            var beforeOk = i == 0 || !char.IsLetterOrDigit(haystack[i - 1]);
            var afterOk = end >= haystack.Length || !char.IsLetterOrDigit(haystack[end]);
            if (beforeOk && afterOk) return true;
            from = i + 1;
        }
    }

    public static int WordCount(string? s)
        => string.IsNullOrWhiteSpace(s) ? 0 : s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Làm sạch text trước khi đưa vào prompt/CSV: bỏ '|' và ký tự điều khiển, gộp khoảng trắng, cắt theo từ.
    /// </summary>
    public static string Sanitize(string? s, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(s) || maxLen <= 0) return string.Empty;

        var sb = new StringBuilder(Math.Min(s.Length, maxLen + 8));
        var lastSpace = false;
        foreach (var c in s)
        {
            var ch = (c == '|' || char.IsControl(c)) ? ' ' : c;
            if (ch == ' ')
            {
                if (lastSpace) continue;
                lastSpace = true;
            }
            else lastSpace = false;
            sb.Append(ch);
        }

        var t = sb.ToString().Trim();
        if (t.Length <= maxLen) return t;

        var cut = t[..maxLen];
        if (cut.Length > 0 && char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        var sp = cut.LastIndexOf(' ');
        if (sp > maxLen / 2) cut = cut[..sp];
        return cut.TrimEnd(',', '.', ';', ':', ' ') + "…";
    }
}

/// <summary>
/// Tập keyword match 2 tầng:
///   1) khớp nguyên từ trên text GIỮ dấu ("gói", "sợ", "vs"...)
///   2) chỉ với keyword đủ dài (≥ 6 ký tự sau khi bỏ dấu) mới khớp thêm trên text KHÔNG dấu
///      → user gõ "so sanh", "phim bo", "dang ky" vẫn nhận ra, nhưng keyword ngắn như "gói"/"sợ"
///      không bị nhầm với "goi y"/"so sanh".
/// </summary>
public sealed class AiKeywordSet
{
    private readonly (string Exact, string? Folded)[] _items;

    public AiKeywordSet(IEnumerable<string> keywords)
    {
        _items = keywords
            .Select(k =>
            {
                var exact  = AiText.Lower(k);
                var folded = AiText.RemoveDiacritics(exact);
                return (Exact: exact, Folded: (folded.Length >= 6 && folded != exact) ? folded : null);
            })
            .Distinct()
            .ToArray();
    }

    /// <param name="lower">Text lowercase, giữ dấu.</param>
    /// <param name="folded">Text lowercase, đã bỏ dấu.</param>
    public int Score(string lower, string folded)
    {
        var n = 0;
        foreach (var (exact, foldedKw) in _items)
        {
            if (AiText.ContainsWord(lower, exact) ||
                (foldedKw is not null && AiText.ContainsWord(folded, foldedKw)))
                n++;
        }
        return n;
    }

    public bool Any(string lower, string folded) => Score(lower, folded) > 0;
}

public static class AiCountries
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = "Mỹ", ["KR"] = "Hàn Quốc", ["JP"] = "Nhật Bản", ["CN"] = "Trung Quốc",
        ["HK"] = "Hồng Kông", ["TW"] = "Đài Loan", ["TH"] = "Thái Lan", ["VN"] = "Việt Nam",
        ["IN"] = "Ấn Độ", ["GB"] = "Anh", ["FR"] = "Pháp", ["DE"] = "Đức", ["ES"] = "Tây Ban Nha",
        ["IT"] = "Ý", ["CA"] = "Canada", ["AU"] = "Úc", ["RU"] = "Nga", ["MX"] = "Mexico", ["BR"] = "Brazil",
    };

    /// <summary>ISO alpha-2 → tên tiếng Việt; không có trong bảng thì trả lại mã gốc.</summary>
    public static string NameOf(string? code)
        => string.IsNullOrWhiteSpace(code) ? "Chưa có dữ liệu"
         : Names.TryGetValue(code.Trim(), out var n) ? n : code.Trim().ToUpperInvariant();
}

/// <summary>
/// Chuẩn hóa tên thể loại về 1 tên chuẩn (tiếng Anh).
/// DB của UIAMovie lưu thể loại tiếng Việt kiểu TMDB ("Phim Hành Động", "Phim Khoa Học Viễn Tưởng"),
/// trong khi MoodGenreMap dùng tên tiếng Anh ("Action") → so sánh chuỗi trực tiếp sẽ KHÔNG BAO GIỜ khớp.
/// Class này map cả hai phía về cùng tên chuẩn. Thể loại kép của TV ("Action &amp; Adventure") được tách theo '&amp;'.
/// </summary>
public static class AiGenres
{
    private static readonly (string Canonical, string[] Aliases)[] Table =
    [
        ("Action",          ["action", "hanh dong"]),
        ("Adventure",       ["adventure", "phieu luu"]),
        ("Animation",       ["animation", "hoat hinh", "anime", "cartoon"]),
        ("Comedy",          ["comedy", "hai", "hai huoc"]),
        ("Crime",           ["crime", "hinh su", "toi pham"]),
        ("Documentary",     ["documentary", "tai lieu"]),
        ("Drama",           ["drama", "chinh kich", "tam ly"]),
        ("Family",          ["family", "gia dinh"]),
        ("Fantasy",         ["fantasy", "gia tuong", "than thoai"]),
        ("History",         ["history", "lich su"]),
        ("Horror",          ["horror", "kinh di"]),
        ("Music",           ["music", "am nhac", "nhac"]),
        ("Mystery",         ["mystery", "bi an", "trinh tham", "huyen bi"]),
        ("Romance",         ["romance", "lang man", "tinh cam"]),
        ("Science Fiction", ["science fiction", "sci fi", "scifi", "khoa hoc vien tuong", "vien tuong"]),
        ("Thriller",        ["thriller", "gay can", "giat gan", "ly ky"]),
        ("War",             ["war", "chien tranh"]),
        ("Western",         ["western", "mien tay"]),
        ("Biography",       ["biography", "tieu su"]),
        ("Sport",           ["sport", "the thao"]),
    ];

    private static readonly Dictionary<string, string> Lookup = BuildLookup();

    private static Dictionary<string, string> BuildLookup()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (canonical, aliases) in Table)
            foreach (var a in aliases)
                d[AiText.Slug(a)] = canonical;
        return d;
    }

    /// <summary>"Phim Khoa Học Viễn Tưởng" / "Sci-Fi &amp; Fantasy" → ["Science Fiction", "Fantasy"].</summary>
    public static IReadOnlyList<string> Canonicalize(string? genreName)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(genreName)) return result;

        foreach (var part in genreName.Split(new[] { '&', '/', '+' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var slug = AiText.Slug(part);
            if (slug.StartsWith("phim ", StringComparison.Ordinal)) slug = slug[5..];
            if (Lookup.TryGetValue(slug, out var canon) && !result.Contains(canon))
                result.Add(canon);
        }
        return result;
    }

    public static HashSet<string> CanonicalSet(IEnumerable<string> genreNames)
        => genreNames.SelectMany(Canonicalize).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Số thể loại của item khớp với danh sách target (target có thể là tên EN hoặc VI).</summary>
    public static int MatchCount(IEnumerable<string> itemGenres, IEnumerable<string> targets)
    {
        var item = CanonicalSet(itemGenres);
        var want = CanonicalSet(targets);
        return want.Count(item.Contains);
    }

    public static bool MatchesAny(IEnumerable<string> itemGenres, IEnumerable<string> targets)
        => MatchCount(itemGenres, targets) > 0;

    /// <summary>Overload cho chuỗi "A, B, C" (MovieContext.Genres).</summary>
    public static bool MatchesAnyCsv(string? genresCsv, IEnumerable<string> targets)
        => MatchesAny((genresCsv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), targets);

    /// <summary>Bỏ tiền tố "Phim " để tiết kiệm token: "Phim Hành Động" → "Hành Động".</summary>
    public static string ToShort(string genre)
    {
        var g = genre.Trim();
        return g.StartsWith("Phim ", StringComparison.OrdinalIgnoreCase) ? g[5..] : g;
    }
}
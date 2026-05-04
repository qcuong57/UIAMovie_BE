// UIAMovie.Application/AI/MoviePrompts.cs

using UIAMovie.Application.Interfaces;

namespace UIAMovie.Application.AI;

/// <summary>
/// Tất cả Groq prompts tập trung tại đây — single source of truth.
///
/// Rules:
///   - System prompt tách hoàn toàn khỏi user/data context
///   - Mỗi prompt có ràng buộc output format rõ ràng
///   - Token budget ghi chú bên cạnh để dễ kiểm soát quota
///
/// [v2] Thêm:
///   - SiteGuideSystem + SiteKnowledge  → trả lời câu hỏi về website
///   - MoodSystem + BuildMoodUser       → gợi ý phim theo tâm trạng
///   - CompareSystem + BuildCompareUser → so sánh 2 phim
///   - ReviewSystem / BuildReviewUser   → tóm tắt đánh giá (giữ nguyên từ v1)
///
/// [v3] Fixes:
///   [FIX-3] DetectIntent: xử lý multi-intent — ưu tiên intent cụ thể nhất,
///           không bỏ sót khi câu hỏi chứa cả mood lẫn compare.
/// </summary>
public static class MoviePrompts
{
    // ─── Chat ─────────────────────────────────────────────────────────────────
    // Token estimate: ~40 tokens system + user message. Output capped at 300 tokens.
    public const string ChatSystem =
        "Bạn là trợ lý AI của UIAMovie. Trả lời bằng tiếng Việt, ngắn gọn, tối đa 3 câu. " +
        "Không lặp câu hỏi. Nếu câu hỏi không liên quan đến phim hoặc website UIAMovie, " +
        "hãy lịch sự từ chối và gợi ý user hỏi về phim hoặc tính năng website.";

    // ─── Site Guide ───────────────────────────────────────────────────────────
    // Token estimate: ~120 tokens system+knowledge + user message. Output capped at 200 tokens.
    public const string SiteGuideSystem =
        "Bạn là trợ lý hỗ trợ người dùng UIAMovie. Trả lời bằng tiếng Việt, " +
        "ngắn gọn và thân thiện. Chỉ trả lời dựa vào thông tin website bên dưới. " +
        "Nếu không tìm thấy thông tin, hãy nói 'Tôi chưa có thông tin về vấn đề này, " +
        "vui lòng liên hệ support@uiamovie.vn'.";

    /// <summary>
    /// Kiến thức tĩnh về website — cập nhật tại đây khi có thay đổi.
    /// Không cần gọi AI nếu câu hỏi match keyword trong SiteKnowledgeKeywords.
    /// </summary>
    public const string SiteKnowledge = """
        === THÔNG TIN WEBSITE UIAMOVIE ===

        [TÀI KHOẢN]
        - Đăng ký: Nhấn nút "Đăng ký" ở góc trên phải, điền email + mật khẩu, xác nhận qua email.
        - Đăng nhập: Nhấn "Đăng nhập", hỗ trợ Google OAuth và email/mật khẩu.
        - Quên mật khẩu: Trang đăng nhập → "Quên mật khẩu?" → nhập email → nhận link đặt lại.
        - Đổi thông tin: Vào Hồ sơ (icon người dùng) → Chỉnh sửa hồ sơ.

        [GÓI DỊCH VỤ]
        - Gói Free (miễn phí): Xem phim có quảng cáo, chất lượng tối đa SD (480p), không tải offline.
        - Gói Premium (99.000đ/tháng hoặc 799.000đ/năm): Không quảng cáo, HD/4K, tải offline, xem trên 2 thiết bị đồng thời.
        - Nâng cấp: Vào Hồ sơ → Nâng cấp Premium → chọn gói → thanh toán.

        [THANH TOÁN]
        - Phương thức: VNPAY, MoMo, ZaloPay, thẻ Visa/Mastercard.
        - Hoàn tiền: Trong vòng 7 ngày nếu chưa sử dụng tính năng Premium.
        - Hóa đơn: Gửi tự động qua email sau khi thanh toán thành công.

        [TÍNH NĂNG]
        - Watchlist (Xem sau): Nhấn icon bookmark trên poster phim hoặc trang chi tiết phim.
        - Lịch sử xem: Hồ sơ → Lịch sử — lưu tự động, tiếp tục xem từ điểm dừng.
        - Đánh giá phim: Vào trang chi tiết phim → cuộn xuống → Viết đánh giá (cần đăng nhập).
        - Tìm kiếm: Ô tìm kiếm trên thanh điều hướng, hỗ trợ tìm theo tên, diễn viên, thể loại.
        - AI Chat: Nút chat góc dưới phải — hỏi về phim, tâm trạng, so sánh phim.

        [HỖ TRỢ KỸ THUẬT]
        - Phim bị giật/lag: Kiểm tra tốc độ mạng, giảm chất lượng trong trình phát video.
        - Không xem được: Xóa cache trình duyệt, thử trình duyệt khác (Chrome/Edge khuyên dùng).
        - Lỗi thanh toán: Kiểm tra số dư, thử phương thức khác, liên hệ support nếu vẫn lỗi.
        - Email hỗ trợ: support@uiamovie.vn — phản hồi trong 24h làm việc.
        """;

    // ─── Recommend ────────────────────────────────────────────────────────────
    // Token estimate: ~20 movies × 120 chars ≈ 700 tokens input. Output: 12 GUIDs ≈ 48 tokens.
    public const string RecommendSystem =
        "Movie recommendation engine. Output ONLY a valid JSON array of UUIDs. No explanation, no markdown.";

    /// <param name="watched">Comma-separated list of watched titles (max 15)</param>
    /// <param name="genres">Comma-separated list of preferred genres</param>
    /// <param name="movieCsv">id|title|genres|rating|description, one entry per line</param>
    public static string BuildRecommendUser(
        string watched,
        string genres,
        string movieCsv) => $"""
        Watched: {watched}
        Preferred genres: {genres}
        Available (id|title|genres|rating|description):
        {movieCsv}
        Rules: exclude watched, prefer genre match, then high rating, use description to understand content. Return EXACTLY 9 UUIDs, no more, no less.
        Output: ["uuid1","uuid2",...]
        """;

    // ─── Mood Recommend ───────────────────────────────────────────────────────
    // Token estimate: ~20 movies × 80 chars ≈ 500 tokens. Output: 8 GUIDs ≈ 35 tokens.
    public const string MoodSystem =
        "Movie mood matcher. Output ONLY a valid JSON array of UUIDs. No explanation, no markdown.";

    /// <summary>
    /// Map tâm trạng → thể loại ưu tiên. Dùng cho cả filter cứng và prompt.
    /// </summary>
    public static readonly Dictionary<string, string[]> MoodGenreMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["buồn"]       = ["Drama", "Romance"],
        ["cô đơn"]     = ["Romance", "Drama"],
        ["vui"]        = ["Comedy", "Animation", "Family"],
        ["hào hứng"]   = ["Action", "Adventure"],
        ["hồi hộp"]    = ["Thriller", "Mystery", "Crime"],
        ["thư giãn"]   = ["Documentary", "Family", "Comedy"],
        ["sợ"]         = ["Horror"],
        ["muốn khóc"]  = ["Drama"],
        ["lãng mạn"]   = ["Romance"],
        ["truyền cảm hứng"] = ["Biography", "Sport", "Drama"],
    };

    /// <param name="mood">Tâm trạng người dùng (đã normalize)</param>
    /// <param name="targetGenres">Thể loại ưu tiên từ MoodGenreMap</param>
    /// <param name="movieCsv">id|title|genres|rating|description</param>
    public static string BuildMoodUser(string mood, string targetGenres, string movieCsv) => $"""
        User mood: "{mood}"
        Preferred genres for this mood: {targetGenres}
        Available (id|title|genres|rating|description):
        {movieCsv}
        Rules: strongly prefer genre match, consider description tone/feel, then high rating. Return EXACTLY 9 UUIDs, no more, no less.
        Output: ["uuid1","uuid2",...]
        """;

    // ─── Smart Search ─────────────────────────────────────────────────────────
    // Token estimate: ~25 movies × 120 chars ≈ 850 tokens input. Output: 15 GUIDs ≈ 60 tokens.
    public const string SearchSystem =
        "Movie search engine. Output ONLY a valid JSON array of UUIDs. No explanation, no markdown.";

    /// <param name="query">Natural language search query from user</param>
    /// <param name="movieCsv">id|title|genres|rating|description, one entry per line</param>
    public static string BuildSearchUser(string query, string movieCsv) => $"""
        Find movies matching: "{query}"
        Catalog (id|title|genres|rating|description):
        {movieCsv}
        Use description to understand movie content. Match by meaning, not just keywords.
        Output: ["uuid1",...] (max 15). Empty array [] if no match.
        """;

    // ─── Review Summary ───────────────────────────────────────────────────────
    // Token estimate: movie title + 5 reviews ≈ 200 tokens input. Output: 1 sentence ≈ 40 tokens.
    public const string ReviewSystem =
        "Summarize movie reviews in ONE Vietnamese sentence, max 25 words. Output only the sentence.";

    /// <param name="title">Movie title</param>
    /// <param name="reviews">User reviews (max 5 taken, each truncated to 120 chars)</param>
    public static string BuildReviewUser(string title, IEnumerable<string> reviews)
    {
        var reviewLines = reviews
            .Take(5)
            .Select((r, i) =>
            {
                var clean     = r.Trim();
                var truncated = clean.Length > 120 ? clean[..120] : clean;
                return $"{i + 1}. {truncated}";
            });

        return $"Movie: {title}\nReviews:\n{string.Join("\n", reviewLines)}";
    }

    // ─── Compare ──────────────────────────────────────────────────────────────
    // Token estimate: 2 movies × ~150 chars ≈ 200 tokens input. Output: markdown table ≈ 150 tokens.
    public const string CompareSystem =
        "So sánh 2 phim bằng tiếng Việt. Xuất ra bảng Markdown đúng chuẩn với 3 cột: " +
        "| Tiêu chí | {TênPhimA} | {TênPhimB} |. Dùng đúng 6 tiêu chí sau: " +
        "Thể loại, Điểm đánh giá, Đạo diễn, Năm sản xuất, Nội dung, Phù hợp với. " +
        "Chỉ xuất bảng Markdown, không thêm giải thích.";

    /// <param name="titleA">Tên phim A</param>
    /// <param name="genresA">Thể loại phim A</param>
    /// <param name="ratingA">Điểm phim A</param>
    /// <param name="directorA">Đạo diễn phim A</param>
    /// <param name="yearA">Năm phim A</param>
    /// <param name="descA">Mô tả phim A (truncated)</param>
    /// <param name="titleB">Tên phim B</param>
    /// <param name="genresB">Thể loại phim B</param>
    /// <param name="ratingB">Điểm phim B</param>
    /// <param name="directorB">Đạo diễn phim B</param>
    /// <param name="yearB">Năm phim B</param>
    /// <param name="descB">Mô tả phim B (truncated)</param>
    public static string BuildCompareUser(
        string titleA, string genresA, double ratingA, string directorA, int? yearA, string descA,
        string titleB, string genresB, double ratingB, string directorB, int? yearB, string descB) => $"""
        Phim A: {titleA}
        - Thể loại: {genresA}
        - Điểm: {ratingA:F1}/10
        - Đạo diễn: {directorA}
        - Năm: {yearA?.ToString() ?? "N/A"}
        - Mô tả: {descA}

        Phim B: {titleB}
        - Thể loại: {genresB}
        - Điểm: {ratingB:F1}/10
        - Đạo diễn: {directorB}
        - Năm: {yearB?.ToString() ?? "N/A"}
        - Mô tả: {descB}
        """;

    // ─── Intent Detection (keyword-based, zero AI call) ───────────────────────

    /// <summary>
    /// [FIX-3] Phân loại intent câu hỏi dựa trên keyword — không tốn token Groq.
    /// Trả về một trong: "movie" | "site" | "mood" | "compare" | "review"
    ///
    /// Multi-intent handling:
    ///   Mỗi intent được tính điểm độc lập. Intent có score cao nhất thắng.
    ///   VD: "Tôi đang buồn, muốn xem phim hay hơn Inception thì chọn gì?"
    ///       → mood score: 2 (buồn + tâm trạng), compare score: 1 (hay hơn)
    ///       → mood thắng → route đúng.
    ///
    ///   Ưu tiên tie-break: compare > review > mood > site > movie
    ///   (compare và review là intent cụ thể hơn mood/site)
    /// </summary>
    public static string DetectIntent(string message, IEnumerable<string>? recentHistory = null)
    {
        var text = (message + " " + string.Join(" ", recentHistory ?? []))
                   .ToLowerInvariant();

        var scores = new Dictionary<string, int>
        {
            ["compare"] = 0,
            ["review"]  = 0,
            ["mood"]    = 0,
            ["site"]    = 0,
        };

        // ── Compare keywords ──────────────────────────────────────────────────
        foreach (var kw in CompareKeywords)
            if (text.Contains(kw)) scores["compare"]++;

        // ── Review keywords ───────────────────────────────────────────────────
        foreach (var kw in ReviewKeywords)
            if (text.Contains(kw)) scores["review"]++;

        // ── Mood keywords ─────────────────────────────────────────────────────
        foreach (var kw in MoodKeywords)
            if (text.Contains(kw)) scores["mood"]++;

        // Mood từ MoodGenreMap cũng tính điểm (ví dụ: "buồn", "vui", "hồi hộp"...)
        foreach (var mood in MoodGenreMap.Keys)
            if (text.Contains(mood)) scores["mood"]++;

        // ── Site keywords ─────────────────────────────────────────────────────
        foreach (var kw in SiteKeywords)
            if (text.Contains(kw)) scores["site"]++;

        // ── Tìm intent thắng ─────────────────────────────────────────────────
        // Nếu không intent nào có điểm → default movie
        var maxScore = scores.Values.Max();
        if (maxScore == 0) return "movie";

        // Tie-break theo thứ tự ưu tiên: compare > review > mood > site
        var priority = new[] { "compare", "review", "mood", "site" };
        foreach (var intent in priority)
        {
            if (scores[intent] == maxScore)
                return intent;
        }

        return "movie";
    }

    // ── Keyword sets (tách ra để dễ maintain) ─────────────────────────────────

    private static readonly string[] CompareKeywords =
    [
        "so sánh", "compare", "khác nhau", "tốt hơn", "hay hơn",
        "giữa", "versus", "vs", "cái nào hơn", "phim nào hay hơn",
    ];

    private static readonly string[] ReviewKeywords =
    [
        "đánh giá", "review", "nhận xét", "người xem nói",
        "ý kiến", "bình luận", "mọi người nghĩ", "cảm nhận",
    ];

    private static readonly string[] MoodKeywords =
    [
        "tâm trạng", "mood", "hôm nay muốn", "muốn xem gì",
        "gợi ý cho tâm trạng", "cảm xúc",
    ];

    private static readonly string[] SiteKeywords =
    [
        "đăng ký", "đăng nhập", "tài khoản", "mật khẩu", "thanh toán", "gói",
        "premium", "subscription", "lỗi", "hướng dẫn", "watchlist", "xem sau",
        "lịch sử", "hỗ trợ", "support", "phí", "miễn phí", "free", "hoàn tiền",
        "invoice", "hóa đơn", "nâng cấp", "upgrade", "quên mật khẩu",
    ];

    private static bool ContainsAny(string text, string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}

// ─── AiMovieCsvBuilder — shared helper, loại bỏ duplicate giữa Controller & Service ───
// [FIX-2] Trước đây BuildMovieCsv tồn tại ở cả AiController lẫn GroqService.
//         Nay tách ra thành static helper dùng chung — single source of truth.

/// <summary>
/// Shared CSV builder cho movie context gửi lên Groq.
/// Format: id|title|genres|rating|description (one movie per line).
/// </summary>
public static class AiMovieCsvBuilder
{
    private const int DescriptionCsvLength = 150;

    public static string Build(List<MovieContext> movies)
    {
        var sb = new System.Text.StringBuilder(movies.Count * 120);
        foreach (var m in movies)
        {
            var desc = m.Description.Length > DescriptionCsvLength
                ? m.Description[..DescriptionCsvLength].Trim()
                : m.Description.Trim();

            // Sanitize: xóa ký tự delimiter và newline khỏi description
            var safeDesc = desc
                .Replace('|', ' ')
                .Replace("\n", " ")
                .Replace("\r", " ");

            sb.AppendLine($"{m.Id}|{m.Title}|{m.Genres}|{m.Rating:F1}|{safeDesc}");
        }
        return sb.ToString();
    }
}
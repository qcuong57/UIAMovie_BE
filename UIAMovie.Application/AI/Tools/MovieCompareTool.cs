using System.Text.Json;
using UIAMovie.Application.AI.Models;
using UIAMovie.Application.AI.Retrieval;
using UIAMovie.Application.DTOs;
using UIAMovie.Application.Interfaces;
using UIAMovie.Infrastructure.AI.Parsing;
using UIAMovie.Infrastructure.AI.Providers;

namespace UIAMovie.Application.AI.Tools;

public class MovieCompareTool : IAiTool
{
    public string Name => "MovieCompareTool";
    private readonly IAiProvider _provider;

    public const string SystemPrompt = """
        Bạn là chuyên gia phê bình điện ảnh sắc sảo của UIAMovie. Nhiệm vụ của bạn là so sánh chuyên sâu 2 tác phẩm được cung cấp.
        Phân tích dựa trên dữ liệu thật (thể loại, đạo diễn, diễn viên, điểm số, tóm tắt). 
        Nhận xét công tâm, thẳng thắn: chỉ ra cái hay, cái dở/hạn chế của từng phim và đưa ra phán quyết nên xem phim nào tùy theo gu khán giả.

        BẮT BUỘC trả về định dạng JSON hợp lệ (không kèm text ngoài JSON) với cấu trúc sau:
        {
          "summary": "Đoạn văn ngắn 2-3 câu tổng quan về cuộc đối đầu giữa 2 tác phẩm",
          "criteria": [
            {
              "name": "Cốt truyện & Kịch bản",
              "movieA": "Nhận xét kịch bản phim A (ngắn gọn, sắc bén)",
              "movieB": "Nhận xét kịch bản phim B (ngắn gọn, sắc bén)"
            },
            {
              "name": "Diễn xuất & Nhân vật",
              "movieA": "Đánh giá diễn xuất A",
              "movieB": "Đánh giá diễn xuất B"
            },
            {
              "name": "Hình ảnh & Trải nghiệm",
              "movieA": "Đánh giá kỹ xảo/nhịp phim A",
              "movieB": "Đánh giá kỹ xảo/nhịp phim B"
            }
          ],
          "highlightA": "Điểm mạnh nổi bật nhất của A",
          "highlightB": "Điểm mạnh nổi bật nhất của B",
          "verdict": "Lời khuyên đúc kết: Nên xem phim nào hơn, trường hợp nào nên chọn A, trường hợp nào nên chọn B."
        }
        """;

    public MovieCompareTool(IAiProvider provider)
    {
        _provider = provider;
    }

    public async Task<AiToolResult> ExecuteAsync(string input, IDictionary<string, object>? parameters = null)
    {
        if (parameters == null || 
            !parameters.TryGetValue("MovieA", out var a) || 
            !parameters.TryGetValue("MovieB", out var b) ||
            a is not MovieDTO movieA || b is not MovieDTO movieB)
        {
            return new AiToolResult { Success = false, ErrorMessage = "Thiếu dữ liệu phim so sánh." };
        }

        var castA = movieA.Cast != null ? string.Join(", ", movieA.Cast.Take(4).Select(c => c.Name)) : "N/A";
        var castB = movieB.Cast != null ? string.Join(", ", movieB.Cast.Take(4).Select(c => c.Name)) : "N/A";

        var userPrompt = $"""
            So sánh 2 tác phẩm sau:
            [Phim A]
            - Tên: {movieA.Title} ({movieA.ReleaseDate?.Year})
            - Thể loại: {string.Join(", ", movieA.Genres)}
            - Điểm IMDb: {movieA.Rating:F1}/10
            - Đạo diễn: {movieA.Director ?? "Chưa rõ"}
            - Diễn viên: {castA}
            - Thời lượng: {movieA.Duration} phút
            - Tóm tắt: {movieA.Description}

            [Phim B]
            - Tên: {movieB.Title} ({movieB.ReleaseDate?.Year})
            - Thể loại: {string.Join(", ", movieB.Genres)}
            - Điểm IMDb: {movieB.Rating:F1}/10
            - Đạo diễn: {movieB.Director ?? "Chưa rõ"}
            - Diễn viên: {castB}
            - Thời lượng: {movieB.Duration} phút
            - Tóm tắt: {movieB.Description}
            """;

        var rawResponse = await _provider.GenerateChatResponseAsync(
            new List<AiProviderMessage>
            {
                new() { Role = "system", Content = SystemPrompt },
                new() { Role = "user", Content = userPrompt }
            },
            new AiProviderOptions { Temperature = 0.4, MaxTokens = 1200, EnforceJsonObject = true });

        var cleanJson = AiJsonParser.ExtractJsonContent(rawResponse);

        return new AiToolResult
        {
            Success = true,
            ToolName = Name,
            Data = cleanJson,
            FormattedContext = cleanJson
        };
    }
}
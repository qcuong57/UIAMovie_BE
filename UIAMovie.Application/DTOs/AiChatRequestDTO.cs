using System.ComponentModel.DataAnnotations;

namespace UIAMovie.Application.DTOs;

/// <summary>
/// DTO cho chat request — hỗ trợ conversation history để AI nhớ ngữ cảnh hội thoại.
///
/// Fix:
///   [1] Thêm History[] — frontend gửi lên toàn bộ lịch sử chat để AI có context đa lượt
///   [2] Validation tự động qua [ApiController] + ModelState, không cần check thủ công
/// </summary>
public class AiChatRequestDTO
{
    [Required(ErrorMessage = "Message không được để trống.")]
    [MaxLength(500, ErrorMessage = "Message không được vượt quá 500 ký tự.")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Lịch sử hội thoại trước đó — frontend quản lý và gửi lên mỗi request.
    /// Mỗi turn gồm role ("user" hoặc "assistant") và nội dung.
    /// Tối đa 20 turns gần nhất để tránh vượt context limit của Groq.
    /// </summary>
    public List<ChatMessageDTO> History { get; set; } = new();
}

/// <summary>
/// Một turn trong lịch sử hội thoại.
/// </summary>
public class ChatMessageDTO
{
    /// <summary>"user" hoặc "assistant"</summary>
    [Required]
    public string Role    { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;
}
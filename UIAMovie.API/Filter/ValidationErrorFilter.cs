using Microsoft.AspNetCore.Mvc;
using UIAMovie.Application.DTOs;

namespace UIAMovie.API.Filters;

public static class ValidationErrorFilter
{
    public static IActionResult Handler(ActionContext context)
    {
        // Gộp tất cả lỗi validation, lấy lỗi đầu tiên làm message chính
        var errors = context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .SelectMany(x => x.Value!.Errors.Select(e => e.ErrorMessage))
            .Where(msg => !string.IsNullOrWhiteSpace(msg))
            .ToList();

        var message = errors.FirstOrDefault() ?? "Dữ liệu không hợp lệ";

        return new BadRequestObjectResult(new ApiErrorResponseDTO
        {
            Message    = message,
            StatusCode = 400,
        });
    }
}
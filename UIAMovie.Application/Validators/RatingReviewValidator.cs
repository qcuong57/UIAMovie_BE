using FluentValidation;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Validators;

public class RatingReviewValidator : AbstractValidator<RatingReviewDTO>
{
    public RatingReviewValidator()
    {
        RuleFor(x => x.MovieId)
            .NotEmpty().WithMessage("ID phim không được để trống"); // ← Guid dùng NotEmpty

        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 10).WithMessage("Đánh giá phải từ 1-10");

        RuleFor(x => x.ReviewText)
            .MaximumLength(1000).WithMessage("Bình luận tối đa 1000 ký tự")
            .When(x => !string.IsNullOrEmpty(x.ReviewText));
    }
}
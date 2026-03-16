using FluentValidation;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Validators;

public class CreateMovieValidator : AbstractValidator<CreateMovieDTO>
{
    public CreateMovieValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Tên phim không được để trống")
            .MaximumLength(255).WithMessage("Tên phim tối đa 255 ký tự");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Mô tả không được để trống")
            .MaximumLength(2000).WithMessage("Mô tả tối đa 2000 ký tự");

        RuleFor(x => x.Duration)
            .GreaterThan(0).WithMessage("Thời lượng phim phải lớn hơn 0")
            .When(x => x.Duration.HasValue);

        RuleFor(x => x.ImdbRating)
            .InclusiveBetween(0, 10).WithMessage("Đánh giá phải từ 0-10")
            .When(x => x.ImdbRating.HasValue);

        // RuleFor(x => x.GenreIds)
        //     .NotEmpty().WithMessage("Phim phải có ít nhất 1 thể loại")
        //     .Must(x => x.All(id => id > 0)).WithMessage("ID thể loại không hợp lệ");
    }
}
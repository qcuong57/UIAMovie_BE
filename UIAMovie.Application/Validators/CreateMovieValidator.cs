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

        // FIX: bỏ NotEmpty — MovieService.CreateMovieAsync tự fallback
        // Description = Title khi rỗng. NotEmpty ở đây chặn request 400 TRƯỚC
        // khi service kịp chạy fallback đó, nên mâu thuẫn với hành vi thực tế.
        // Vẫn giữ MaxLength để tránh input rác quá dài.
        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Mô tả tối đa 2000 ký tự");

        RuleFor(x => x.Duration)
            .GreaterThan(0).WithMessage("Thời lượng phim phải lớn hơn 0")
            .When(x => x.Duration.HasValue);

        RuleFor(x => x.ImdbRating)
            .InclusiveBetween(0, 10).WithMessage("Đánh giá phải từ 0-10")
            .When(x => x.ImdbRating.HasValue);

        // FIX: GenreIds không được chứa Guid.Empty — dự phòng FE gửi thiếu/serialize
        // lỗi thành Guid mặc định. Không bắt buộc NotEmpty cả list vì phim có thể
        // tạm thời chưa gắn thể loại (đã comment sẵn ở bản cũ, giữ nguyên ý đó).
        RuleForEach(x => x.GenreIds)
            .NotEqual(Guid.Empty).WithMessage("ID thể loại không hợp lệ");

        // FIX: Cast[].Name không được rỗng — UpsertPersonAsync match theo Name khi
        // không có TmdbPersonId; Name rỗng sẽ tạo Person "" hoặc gộp nhầm nhiều Cast
        // rỗng khác nhau vào cùng 1 Person.
        RuleForEach(x => x.Cast)
            .ChildRules(cast =>
            {
                cast.RuleFor(c => c.Name)
                    .NotEmpty().WithMessage("Tên diễn viên không được để trống")
                    .MaximumLength(255).WithMessage("Tên diễn viên tối đa 255 ký tự");
            })
            .When(x => x.Cast.Any());

        // FIX: Director.Name không được rỗng — cùng lý do như Cast ở trên.
        RuleFor(x => x.Director!.Name)
            .NotEmpty().WithMessage("Tên đạo diễn không được để trống")
            .MaximumLength(255).WithMessage("Tên đạo diễn tối đa 255 ký tự")
            .When(x => x.Director != null);

        // FIX: Images[].Url không được rỗng — SaveImagesAsync insert thẳng vào DB
        // không check, Url rỗng sẽ tạo MovieImage vô nghĩa.
        RuleForEach(x => x.Images)
            .ChildRules(image =>
            {
                image.RuleFor(i => i.Url)
                    .NotEmpty().WithMessage("URL ảnh không được để trống");
            })
            .When(x => x.Images.Any());

        // FIX: Trailers[].YoutubeUrl không được rỗng — SaveTrailersAsync insert thẳng
        // vào MovieVideo, Url rỗng sẽ tạo video "trailer" không phát được.
        RuleForEach(x => x.Trailers)
            .ChildRules(trailer =>
            {
                trailer.RuleFor(t => t.YoutubeUrl)
                    .NotEmpty().WithMessage("URL trailer không được để trống");
            })
            .When(x => x.Trailers.Any());
    }
}
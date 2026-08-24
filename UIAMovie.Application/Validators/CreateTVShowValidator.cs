using FluentValidation;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Validators;

public class CreateTvShowValidator : AbstractValidator<CreateTvShowDTO>
{
    public CreateTvShowValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Tên TV show không được để trống")
            .MaximumLength(255).WithMessage("Tên TV show tối đa 255 ký tự");

        // Không NotEmpty — TvShowService.CreateTvShowAsync tự fallback
        // Description = Title khi rỗng, giống CreateMovieValidator.
        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Mô tả tối đa 2000 ký tự");

        RuleFor(x => x.EpisodeRuntime)
            .GreaterThan(0).WithMessage("Thời lượng mỗi tập phải lớn hơn 0")
            .When(x => x.EpisodeRuntime.HasValue);

        RuleFor(x => x.ImdbRating)
            .InclusiveBetween(0, 10).WithMessage("Đánh giá phải từ 0-10")
            .When(x => x.ImdbRating.HasValue);

        RuleFor(x => x.NumberOfSeasons)
            .GreaterThan(0).WithMessage("Số mùa phải lớn hơn 0")
            .When(x => x.NumberOfSeasons.HasValue);

        RuleFor(x => x.NumberOfEpisodes)
            .GreaterThan(0).WithMessage("Số tập phải lớn hơn 0")
            .When(x => x.NumberOfEpisodes.HasValue);

        // GenreIds không được chứa Guid.Empty — dự phòng FE gửi thiếu/serialize lỗi.
        RuleForEach(x => x.GenreIds)
            .NotEqual(Guid.Empty).WithMessage("ID thể loại không hợp lệ");

        // Cast[].Name không được rỗng — UpsertPersonAsync match theo Name khi
        // không có TmdbPersonId; Name rỗng sẽ tạo Person "" hoặc gộp nhầm Cast.
        RuleForEach(x => x.Cast)
            .ChildRules(cast =>
            {
                cast.RuleFor(c => c.Name)
                    .NotEmpty().WithMessage("Tên diễn viên không được để trống")
                    .MaximumLength(255).WithMessage("Tên diễn viên tối đa 255 ký tự");
            })
            .When(x => x.Cast.Any());

        RuleFor(x => x.Director!.Name)
            .NotEmpty().WithMessage("Tên đạo diễn không được để trống")
            .MaximumLength(255).WithMessage("Tên đạo diễn tối đa 255 ký tự")
            .When(x => x.Director != null);

        // Images[].Url không được rỗng — SaveImagesAsync insert thẳng vào DB không check.
        RuleForEach(x => x.Images)
            .ChildRules(image =>
            {
                image.RuleFor(i => i.Url)
                    .NotEmpty().WithMessage("URL ảnh không được để trống");
            })
            .When(x => x.Images.Any());

        // Trailers[].YoutubeUrl không được rỗng — SaveTrailersAsync insert thẳng vào TvShowVideo.
        RuleForEach(x => x.Trailers)
            .ChildRules(trailer =>
            {
                trailer.RuleFor(t => t.YoutubeUrl)
                    .NotEmpty().WithMessage("URL trailer không được để trống");
            })
            .When(x => x.Trailers.Any());

        // ── Seasons / Episodes — riêng của TV show, Movie không có ──────────────
        RuleForEach(x => x.Seasons)
            .ChildRules(season =>
            {
                season.RuleFor(s => s.SeasonNumber)
                    .GreaterThan(0).WithMessage("Số mùa (SeasonNumber) phải lớn hơn 0");

                season.RuleForEach(s => s.Episodes)
                    .ChildRules(ep =>
                    {
                        ep.RuleFor(e => e.EpisodeNumber)
                            .GreaterThan(0).WithMessage("Số tập (EpisodeNumber) phải lớn hơn 0");

                        ep.RuleFor(e => e.Title)
                            .NotEmpty().WithMessage("Tên tập không được để trống")
                            .MaximumLength(255).WithMessage("Tên tập tối đa 255 ký tự");
                    });
            })
            .When(x => x.Seasons.Any());
    }
}
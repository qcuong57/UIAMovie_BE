using FluentValidation;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Validators;

public class RegisterValidator : AbstractValidator<RegisterDTO>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email không được để trống")
            .EmailAddress().WithMessage("Email không hợp lệ")
            .MaximumLength(255).WithMessage("Email tối đa 255 ký tự");

        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username không được để trống")
            .Length(3, 50).WithMessage("Username phải từ 3-50 ký tự")
            .Matches(@"^[a-zA-Z0-9_]+$").WithMessage("Username chỉ được chứa chữ, số, dấu gạch dưới");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Mật khẩu không được để trống")
            .MinimumLength(8).WithMessage("Mật khẩu tối thiểu 8 ký tự")
            .Matches(@"[A-Z]").WithMessage("Mật khẩu phải chứa ít nhất 1 chữ hoa")
            .Matches(@"[a-z]").WithMessage("Mật khẩu phải chứa ít nhất 1 chữ thường")
            .Matches(@"[0-9]").WithMessage("Mật khẩu phải chứa ít nhất 1 số")
            .Matches(@"[!@#$%^&*]").WithMessage("Mật khẩu phải chứa ít nhất 1 ký tự đặc biệt");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password).WithMessage("Xác nhận mật khẩu không khớp");
    }
}
using FluentValidation;
using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Validators;

public class ResetPasswordValidator : AbstractValidator<ResetPasswordDTO>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email không được để trống")
            .EmailAddress().WithMessage("Email không hợp lệ");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Mã OTP không được để trống")
            .Length(6).WithMessage("Mã OTP phải gồm 6 chữ số");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Mật khẩu mới không được để trống")
            .MinimumLength(8).WithMessage("Mật khẩu tối thiểu 8 ký tự")
            .Matches(@"[A-Z]").WithMessage("Mật khẩu phải chứa ít nhất 1 chữ hoa")
            .Matches(@"[a-z]").WithMessage("Mật khẩu phải chứa ít nhất 1 chữ thường")
            .Matches(@"[0-9]").WithMessage("Mật khẩu phải chứa ít nhất 1 số")
            .Matches(@"[!@#$%^&*]").WithMessage("Mật khẩu phải chứa ít nhất 1 ký tự đặc biệt (!@#$%^&*)");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword).WithMessage("Xác nhận mật khẩu không khớp");
    }
}
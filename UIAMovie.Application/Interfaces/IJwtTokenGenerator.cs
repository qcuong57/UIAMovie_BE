// UIAMovie.Application/Interfaces/IJwtTokenGenerator.cs

namespace UIAMovie.Application.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(Guid userId, string email, string role);

    /// <summary>Thời hạn access token (đọc từ cấu hình), dùng chung cho token và ExpiresIn trả về FE.</summary>
    TimeSpan AccessTokenLifetime { get; }

    string GenerateRefreshToken();
}
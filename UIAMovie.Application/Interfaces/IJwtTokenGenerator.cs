// UIAMovie.Application/Interfaces/IJwtTokenGenerator.cs

namespace UIAMovie.Application.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(Guid userId, string email, string role);

    string GenerateRefreshToken();
}
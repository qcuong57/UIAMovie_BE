using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IServices;

namespace UIAMovie.Infrastructure.Security;

public class JwtTokenGenerator : IJwtTokenGenerator
{
    private const int DefaultAccessTokenMinutes = 15;
    private const int MaxAccessTokenMinutes = 60;

    private readonly IConfiguration _configuration;

    public JwtTokenGenerator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // Jwt:AccessTokenMinutes: mặc định 15 phút, tối đa 60 phút (giá trị lớn hơn bị cắt xuống 60).
    public TimeSpan AccessTokenLifetime
    {
        get
        {
            var minutes = int.TryParse(_configuration["Jwt:AccessTokenMinutes"], out var m) && m > 0
                ? m
                : DefaultAccessTokenMinutes;
            return TimeSpan.FromMinutes(Math.Min(minutes, MaxAccessTokenMinutes));
        }
    }

    public string GenerateAccessToken(Guid userId, string email, string role)
    {
        var secretKey = _configuration["Jwt:SecretKey"];
        var issuer    = _configuration["Jwt:Issuer"];
        var audience  = _configuration["Jwt:Audience"];

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(secretKey!);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Role, role)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject            = new ClaimsIdentity(claims),
            IssuedAt           = DateTime.UtcNow, // claim iat: so với mốc thu hồi token (TokenRevocation)
            Expires            = DateTime.UtcNow.Add(AccessTokenLifetime),
            Issuer             = issuer,
            Audience           = audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }
}
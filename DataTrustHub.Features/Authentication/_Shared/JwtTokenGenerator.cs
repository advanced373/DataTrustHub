using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace DataTrustHub.Features.Authentication._Shared;

public class JwtTokenGenerator(IOptions<JwtConfig> jwtOptions) : IJwtTokenGenerator
{
    private readonly JwtConfig _config = jwtOptions.Value;

    public string Generate(Guid userId, string email)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = BuildClaims(userId, email);
        var token = BuildToken(claims, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static Claim[] BuildClaims(Guid userId, string email) =>
    [
        new(JwtRegisteredClaimNames.Sub, userId.ToString()),
        new(JwtRegisteredClaimNames.Email, email),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    ];

    private JwtSecurityToken BuildToken(Claim[] claims, SigningCredentials credentials) =>
        new(
            issuer: _config.Issuer,
            audience: _config.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_config.ExpiryMinutes),
            signingCredentials: credentials);
}

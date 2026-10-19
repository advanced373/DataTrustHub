using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace DataTrustHub.Features._Shared.Jwt;

public class JwtTokenGenerator(IOptions<JwtConfig> jwtOptions) : IJwtTokenGenerator
{
    // Short JWT claim name; JwtBearer maps it to ClaimTypes.Role on the way in
    private const string RoleClaimType = "role";

    private readonly JwtConfig _config = jwtOptions.Value;

    public string Generate(Guid userId, string email, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = BuildClaims(userId, email, role);
        var token = BuildToken(claims, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static Claim[] BuildClaims(Guid userId, string email, string role) =>
    [
        new(JwtRegisteredClaimNames.Sub, userId.ToString()),
        new(JwtRegisteredClaimNames.Email, email),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new(RoleClaimType, role)
    ];

    private JwtSecurityToken BuildToken(Claim[] claims, SigningCredentials credentials) =>
        new(
            issuer: _config.Issuer,
            audience: _config.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_config.ExpiryMinutes),
            signingCredentials: credentials);
}

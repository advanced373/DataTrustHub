using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Services.Session;

public interface IUserSessionService
{
    Task SignInAsync(string accessToken);

    Task SignOutAsync();
}

// Keeps the API token inside the encrypted, HttpOnly authentication cookie.
// The token is only read here, never trusted for authorization: the API validates it on every call.
internal sealed class UserSessionService(IHttpContextAccessor httpContextAccessor) : IUserSessionService
{
    private HttpContext HttpContext =>
        httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No active HTTP context.");

    public Task SignInAsync(string accessToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        var properties = new AuthenticationProperties { ExpiresUtc = jwt.ValidTo, IsPersistent = false };

        return HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            BuildPrincipal(jwt, accessToken),
            properties);
    }

    public Task SignOutAsync() =>
        HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    private static ClaimsPrincipal BuildPrincipal(JwtSecurityToken jwt, string accessToken)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, jwt.Subject),
            new(ClaimTypes.Name, ReadClaim(jwt, JwtRegisteredClaimNames.Email)),
            new(Constants.AccessTokenClaim, accessToken)
        };
        claims.AddRange(ReadRoles(jwt));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    private static string ReadClaim(JwtSecurityToken jwt, string type) =>
        jwt.Claims.FirstOrDefault(claim => claim.Type == type)?.Value ?? string.Empty;

    private static IEnumerable<Claim> ReadRoles(JwtSecurityToken jwt) =>
        jwt.Claims
            .Where(claim => claim.Type is Constants.JwtRoleClaim or ClaimTypes.Role)
            .Select(claim => new Claim(ClaimTypes.Role, claim.Value));
}

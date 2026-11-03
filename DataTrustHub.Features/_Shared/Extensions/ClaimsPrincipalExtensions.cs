using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features._Shared.Extensions;

internal static class ClaimsPrincipalExtensions
{
    // The caller's user id from the JWT; null when missing or not a Guid.
    // The JWT handler may map the sub claim to NameIdentifier on inbound tokens, so both are read
    internal static Guid? GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}

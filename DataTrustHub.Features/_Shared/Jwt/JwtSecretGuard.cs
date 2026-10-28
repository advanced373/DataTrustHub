using Microsoft.Extensions.Configuration;
using System.Text;

namespace DataTrustHub.Features._Shared.Jwt;

// Stops the API at startup when the JWT signing key is missing or too short for HMAC-SHA256
internal static class JwtSecretGuard
{
    internal static void EnsureValid(IConfiguration configuration)
    {
        var secret = configuration[JwtConstants.SecretKey];

        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(JwtConstants.SecretMissingMessage);

        if (Encoding.UTF8.GetByteCount(secret) < JwtConstants.MinimumSecretBytes)
            throw new InvalidOperationException(JwtConstants.SecretTooShortMessage);
    }
}

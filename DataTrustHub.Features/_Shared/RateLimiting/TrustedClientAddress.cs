using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace DataTrustHub.Features._Shared.RateLimiting;

// The client address of a register call (ADR-0026). The WebApp calls the API from its own server, so it sends
// the browser's address in ClientAddressHeader together with the shared key in ClientKeyHeader. The header
// address is used only when a key is configured, the request carries exactly that key and the address parses;
// otherwise the connection's address is used, silently. A direct caller without the key cannot choose its
// address. The header values are never logged
internal static class TrustedClientAddress
{
    internal const string ClientAddressHeader = "X-DTH-Client-Address";
    internal const string ClientKeyHeader = "X-DTH-Client-Key";

    internal static IPAddress? Resolve(HttpContext httpContext, string? trustedClientKey) =>
        TryReadTrustedAddress(httpContext.Request.Headers, trustedClientKey)
        ?? httpContext.Connection.RemoteIpAddress;

    private static IPAddress? TryReadTrustedAddress(IHeaderDictionary headers, string? trustedClientKey)
    {
        if (string.IsNullOrWhiteSpace(trustedClientKey) || !CarriesKey(headers[ClientKeyHeader], trustedClientKey))
            return null;

        var address = headers[ClientAddressHeader];
        return address.Count == 1 && IPAddress.TryParse(address[0], out var parsed) ? parsed : null;
    }

    // Constant-time comparison of the UTF-8 bytes, so the time taken does not tell how much of a guess matched;
    // keys of different lengths are not equal
    private static bool CarriesKey(StringValues sentKey, string trustedClientKey) =>
        sentKey.Count == 1
        && sentKey[0] is { } value
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(trustedClientKey));
}

using System.Net;

namespace DataTrustHub.Services.Api;

// The browser's address for the API's register limit (ADR-0026): the WebApp calls the API from its own server,
// so it sends the browser's address together with the shared client key, and the API trusts the address only
// with that key. Without a key or without an address neither header is sent
internal static class TrustedClientHeaders
{
    internal static void AddTo(HttpRequestMessage message, IPAddress? clientAddress, string? clientKey)
    {
        if (string.IsNullOrWhiteSpace(clientKey) || clientAddress is null) return;

        message.Headers.TryAddWithoutValidation(ApiHeaders.ClientAddress, clientAddress.ToString());
        message.Headers.TryAddWithoutValidation(ApiHeaders.ClientKey, clientKey);
    }
}

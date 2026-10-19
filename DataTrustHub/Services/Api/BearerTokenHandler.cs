using System.Net.Http.Headers;
using System.Security.Claims;

namespace DataTrustHub.Services.Api;

// Attaches the signed-in user's API token to outgoing API requests
internal sealed class BearerTokenHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = httpContextAccessor.HttpContext?.User.FindFirstValue(Constants.AccessTokenClaim);
        if (!string.IsNullOrEmpty(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue(Constants.BearerScheme, accessToken);

        return base.SendAsync(request, cancellationToken);
    }
}

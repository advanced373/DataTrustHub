extern alias WebApp;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.Health.Helpers;

// The WebApp with an API that must never be called: every outgoing HTTP request of its API clients is
// counted and answered 503, so no test reaches the network
public sealed class WebAppFactory : WebApplicationFactory<WebApp::Program>
{
    // Required by the WebApp at startup (ValidateOnStart); .invalid never resolves (RFC 2606)
    private const string ApiBaseUrlKey = "Api:BaseUrl";
    private const string UnreachableApiBaseUrl = "https://api.invalid/";

    internal TouchCounter ApiCalls { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(ApiBaseUrlKey, UnreachableApiBaseUrl);
        builder.ConfigureServices(services =>
            services.ConfigureHttpClientDefaults(client =>
                client.ConfigurePrimaryHttpMessageHandler(() => new CountingApiHandler(ApiCalls))));
    }

    private sealed class CountingApiHandler(TouchCounter apiCalls) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            apiCalls.Touch();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        }
    }
}

extern alias WebApp;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.WebApp.Helpers;

// The WebApp with its real API clients, whose HTTP requests are answered by a StubApi instead of the network,
// so a test exercises the clients' status-code handling together with the controllers and the views
public sealed class StubbedApiWebAppFactory : WebApplicationFactory<WebApp::Program>
{
    // Required by the WebApp at startup (ValidateOnStart); .invalid never resolves (RFC 2606)
    private const string ApiBaseUrlKey = "Api:BaseUrl";
    private const string UnreachableApiBaseUrl = "https://api.invalid/";

    internal StubApi Api { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(ApiBaseUrlKey, UnreachableApiBaseUrl);
        builder.ConfigureServices(services =>
            services.ConfigureHttpClientDefaults(client =>
                client.ConfigurePrimaryHttpMessageHandler(() => new StubApiHandler(Api))));
    }

    private sealed class StubApiHandler(StubApi api) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            api.SendAsync(request, cancellationToken);
    }
}

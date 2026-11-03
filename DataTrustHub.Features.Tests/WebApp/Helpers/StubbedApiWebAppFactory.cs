extern alias WebApp;

using DataTrustHub.Features.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.WebApp.Helpers;

// The WebApp with its real API clients, whose HTTP requests are answered by a StubApi instead of the network,
// so a test exercises the clients' status-code handling together with the controllers and the views.
// The client key shared with the API (Api:ClientKey) is empty unless a subclass gives one; a browser request
// may name its address in BrowserAddress.Header (TestServer gives none). Every log entry is captured
public class StubbedApiWebAppFactory : WebApplicationFactory<WebApp::Program>
{
    // Required by the WebApp at startup (ValidateOnStart); .invalid never resolves (RFC 2606)
    private const string ApiBaseUrlKey = "Api:BaseUrl";
    private const string UnreachableApiBaseUrl = "https://api.invalid/";
    internal const string ApiClientKeyKey = "Api:ClientKey";

    private readonly string clientKey;

    public StubbedApiWebAppFactory() : this(string.Empty)
    {
    }

    // Empty, not missing, so a key from User Secrets on this machine cannot leak in
    protected StubbedApiWebAppFactory(string clientKey)
    {
        this.clientKey = clientKey;
    }

    internal StubApi Api { get; } = new();

    internal CapturedLogs Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(ApiBaseUrlKey, UnreachableApiBaseUrl);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [ApiClientKeyKey] = clientKey }));
        builder.ConfigureLogging(Logs.AddTo);
        builder.ConfigureServices(services =>
        {
            services.ConfigureHttpClientDefaults(client =>
                client.ConfigurePrimaryHttpMessageHandler(() => new StubApiHandler(Api)));
            BrowserAddress.AddTo(services);
        });
    }

    private sealed class StubApiHandler(StubApi api) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            api.SendAsync(request, cancellationToken);
    }
}

// The WebApp with the client key set, as in production after infra/setup.ps1 (#172 PR 4)
public sealed class ClientKeyWebAppFactory() : StubbedApiWebAppFactory(Key)
{
    // Distinctive, so a leak of it is found in logs and pages
    internal const string Key = "WebAppTestClientKey-7f3c9a1e5b2d4086a8c1e9f0d2b4a6c8";
}

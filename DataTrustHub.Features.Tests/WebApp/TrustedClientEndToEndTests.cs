extern alias WebApp;

using System.Net;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.WebApp.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.WebApp.Helpers.WebAppPages;

namespace DataTrustHub.Features.Tests.WebApp;

// #172 PR 4, AC1 end to end: browsers register through the real WebApp, whose API calls reach the real API
// host (no stub). With the same key on both, each browser address gets its own PermitsPerHourPerIp budget;
// with no key on the WebApp, every browser shares the WebApp server's one budget (the problem being fixed)
public sealed class TrustedClientEndToEndTests
{
    private const int Permits = 2;
    private const string SharedKey = "EndToEndSharedClientKey-0123456789abcdefABCDEF";
    private const string FirstBrowser = "203.0.113.31";
    private const string SecondBrowser = "203.0.113.32";

    [Fact]
    public async Task Register_SameKeyOnBothApps_EachBrowserGetsItsOwnBudget()
    {
        await using var apps = await AppsAsync(apiKey: SharedKey, webAppKey: SharedKey);
        var first = await RegisterManyAsync(apps, FirstBrowser, Permits + 1);

        var second = await RegisterManyAsync(apps, SecondBrowser, Permits);

        Assert.Equal([.. Enumerable.Repeat(true, Permits), false], first);
        Assert.All(second, accepted => Assert.True(accepted));
    }

    // Without the key on the WebApp, the API cannot tell the browsers apart
    [Fact]
    public async Task Register_NoKeyOnTheWebApp_BrowsersShareOneBudget()
    {
        await using var apps = await AppsAsync(apiKey: SharedKey, webAppKey: string.Empty);
        await RegisterManyAsync(apps, FirstBrowser, Permits);

        var second = await RegisterManyAsync(apps, SecondBrowser, 1);

        Assert.Equal([false], second);
    }

    // Keys that differ between the apps: the address is not trusted, so the browsers share one budget
    [Fact]
    public async Task Register_DifferentKeysOnTheApps_BrowsersShareOneBudget()
    {
        await using var apps = await AppsAsync(apiKey: SharedKey, webAppKey: SharedKey.ToLowerInvariant());
        await RegisterManyAsync(apps, FirstBrowser, Permits);

        var second = await RegisterManyAsync(apps, SecondBrowser, 1);

        Assert.Equal([false], second);
    }

    // --- helpers ---

    private static async Task<Apps> AppsAsync(string apiKey, string webAppKey)
    {
        var api = await TrustedClientApiFactory.StartAsync(apiKey, Permits);
        return new Apps(api, new WebAppOnApi(api, webAppKey));
    }

    // Registers count times from one browser; true for each sign-up the WebApp reports as sent
    private static async Task<List<bool>> RegisterManyAsync(Apps apps, string browserAddress, int count)
    {
        var client = apps.WebApp.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        BrowserAddress.Set(client, browserAddress);
        var accepted = new List<bool>();
        for (var i = 0; i < count; i++)
        {
            var response = await TrustedClientContract.RegisterThroughThePageAsync(client, $"e2e_{Guid.NewGuid():N}@example.com");
            accepted.Add(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.OriginalString == LoginPath);
        }
        return accepted;
    }

    private sealed class Apps(TrustedClientApiFactory api, WebAppOnApi webApp) : IAsyncDisposable
    {
        public WebAppOnApi WebApp { get; } = webApp;

        public async ValueTask DisposeAsync()
        {
            await WebApp.DisposeAsync();
            await api.StopAsync();
        }
    }

    // The WebApp whose API clients call the given API host in memory
    private sealed class WebAppOnApi(TrustedClientApiFactory api, string clientKey) : WebApplicationFactory<WebApp::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Api:BaseUrl", "https://api.invalid/");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { [StubbedApiWebAppFactory.ApiClientKeyKey] = clientKey }));
            builder.ConfigureServices(services =>
            {
                services.ConfigureHttpClientDefaults(client =>
                    client.ConfigurePrimaryHttpMessageHandler(() => api.Server.CreateHandler()));
                BrowserAddress.AddTo(services);
            });
        }
    }
}

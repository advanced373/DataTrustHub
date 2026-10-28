using System.Net;
using DataTrustHub.Features.Tests.Health.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace DataTrustHub.Features.Tests.Health;

// #146 AC2 and AC3 on the WebApp: GET /health answers 200 without a signed-in user, despite the global
// AuthorizeFilter, without calling the API and without antiforgery; the pages still require sign-in
public class WebAppHealthTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private const string HomePage = "/";
    private const string LoginPath = "/Account/Login";

    [Fact]
    public async Task Health_Anonymous_Returns200()
    {
        var response = await CreateClient().GetAsync(HealthProbe.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_Anonymous_IsNotRedirectedToLogin()
    {
        var response = await CreateClient().GetAsync(HealthProbe.Route);

        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Health_Anonymous_BodyMeansHealthy()
    {
        var response = await CreateClient().GetAsync(HealthProbe.Route);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(HealthProbe.HealthyBody, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_Anonymous_DoesNotCallTheApi()
    {
        await CreateClient().GetAsync(HealthProbe.Route);

        Assert.Equal(0, factory.ApiCalls.Count);
    }

    // Antiforgery would issue its cookie and the cookie authentication a sign-in redirect
    [Fact]
    public async Task Health_Anonymous_SetsNoCookie()
    {
        var response = await CreateClient().GetAsync(HealthProbe.Route);

        Assert.False(response.Headers.Contains(HeaderNames.SetCookie));
    }

    [Fact]
    public async Task HomePage_Anonymous_StillRedirectsToLogin()
    {
        var response = await CreateClient().GetAsync(HomePage);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(LoginPath, response.Headers.Location!.OriginalString);
    }

    private HttpClient CreateClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}

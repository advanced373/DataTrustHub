extern alias WebApp;

using System.Net;
using DataTrustHub.Features.Tests.WebApp.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WebApp::DataTrustHub.Services.Api;
using Xunit;
using static DataTrustHub.Features.Tests.WebApp.Helpers.WebAppPages;

namespace DataTrustHub.Features.Tests.WebApp;

// The header names of the contract (#172 PR 4), shared by name only between the WebApp and the API
internal static class TrustedClientContract
{
    internal const string ClientAddressHeader = "X-DTH-Client-Address";
    internal const string ClientKeyHeader = "X-DTH-Client-Key";

    internal const string Email = "trusted.client@datatrusthub.test";
    internal const string Password = "Secret-Pass-123";
    internal const string BrowserIpv4 = "203.0.113.25";
    internal const string BrowserIpv6 = "2001:db8::25";

    internal static bool CarriesEither(RecordedApiRequest request) =>
        request.HeaderValues(ClientAddressHeader).Length > 0 || request.HeaderValues(ClientKeyHeader).Length > 0;

    internal static Task<HttpResponseMessage> RegisterThroughThePageAsync(HttpClient client, string email = Email) =>
        PostFormAsync(client, RegisterPath, RegisterPath, new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = Password,
            ["ConfirmPassword"] = Password
        });
}

// #172 PR 4, AC4 and the WebApp side of AC5, with Api:ClientKey set: the register call carries the browser's
// address and the key; no other call carries either header; the key reaches neither the logs nor a page
public class TrustedClientHeadersWithKeyTests : IClassFixture<ClientKeyWebAppFactory>, IDisposable
{
    private readonly ClientKeyWebAppFactory factory;
    private readonly IServiceScope scope;
    private readonly IAuthApiClient authApi;
    private readonly IHttpContextAccessor httpContextAccessor;

    public TrustedClientHeadersWithKeyTests(ClientKeyWebAppFactory factory)
    {
        this.factory = factory;
        factory.Api.Reset();
        scope = factory.Services.CreateScope();
        authApi = scope.ServiceProvider.GetRequiredService<IAuthApiClient>();
        httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
    }

    public void Dispose()
    {
        httpContextAccessor.HttpContext = null;
        scope.Dispose();
    }

    // --- AC4 at the API client ---

    [Theory]
    [InlineData(TrustedClientContract.BrowserIpv4)]
    [InlineData(TrustedClientContract.BrowserIpv6)]
    public async Task RegisterAsync_KeySetAndBrowserAddressKnown_SendsTheBrowserAddress(string browserAddress)
    {
        ServeBrowser(IPAddress.Parse(browserAddress));

        var request = await RegisterThroughTheClientAsync();

        Assert.Equal([IPAddress.Parse(browserAddress).ToString()], request.HeaderValues(TrustedClientContract.ClientAddressHeader));
    }

    [Fact]
    public async Task RegisterAsync_KeySetAndBrowserAddressKnown_SendsTheKey()
    {
        ServeBrowser(IPAddress.Parse(TrustedClientContract.BrowserIpv4));

        var request = await RegisterThroughTheClientAsync();

        Assert.Equal([ClientKeyWebAppFactory.Key], request.HeaderValues(TrustedClientContract.ClientKeyHeader));
    }

    // Without the browser's address there is nothing to pass on: neither header
    [Fact]
    public async Task RegisterAsync_BrowserAddressUnknown_SendsNeitherHeader()
    {
        ServeBrowser(address: null);

        var request = await RegisterThroughTheClientAsync();

        Assert.False(TrustedClientContract.CarriesEither(request));
    }

    [Fact]
    public async Task RegisterAsync_OutsideABrowserRequest_SendsNeitherHeader()
    {
        var request = await RegisterThroughTheClientAsync();

        Assert.False(TrustedClientContract.CarriesEither(request));
    }

    [Fact]
    public async Task LoginAsync_KeySetAndBrowserAddressKnown_SendsNeitherHeader()
    {
        ServeBrowser(IPAddress.Parse(TrustedClientContract.BrowserIpv4));
        factory.Api.Answer(HttpMethod.Post, ApiLogin, HttpStatusCode.Unauthorized);

        await authApi.LoginAsync(TrustedClientContract.Email, TrustedClientContract.Password, CancellationToken.None);

        Assert.False(TrustedClientContract.CarriesEither(Assert.Single(factory.Api.Requests)));
    }

    // --- AC4 through the pages: the browser's own address, register only ---

    [Fact]
    public async Task RegisterPage_KeySet_SendsTheRequestingBrowsersAddressAndTheKey()
    {
        var client = BrowserClient(TrustedClientContract.BrowserIpv4);
        AnswerRegister();

        await TrustedClientContract.RegisterThroughThePageAsync(client);

        var request = Assert.Single(factory.Api.Requests, request => request.Path == ApiRegister);
        Assert.Equal([TrustedClientContract.BrowserIpv4], request.HeaderValues(TrustedClientContract.ClientAddressHeader));
        Assert.Equal([ClientKeyWebAppFactory.Key], request.HeaderValues(TrustedClientContract.ClientKeyHeader));
    }

    // Two browsers, each register call names its own browser
    [Fact]
    public async Task RegisterPage_TwoBrowsers_EachCallNamesItsOwnBrowser()
    {
        AnswerRegister();

        await TrustedClientContract.RegisterThroughThePageAsync(BrowserClient(TrustedClientContract.BrowserIpv4), "first@datatrusthub.test");
        await TrustedClientContract.RegisterThroughThePageAsync(BrowserClient(TrustedClientContract.BrowserIpv6), "second@datatrusthub.test");

        var addresses = factory.Api.Requests.Where(request => request.Path == ApiRegister)
            .Select(request => Assert.Single(request.HeaderValues(TrustedClientContract.ClientAddressHeader))).ToList();
        Assert.Equal([TrustedClientContract.BrowserIpv4, IPAddress.Parse(TrustedClientContract.BrowserIpv6).ToString()], addresses);
    }

    [Fact]
    public async Task LoginPage_KeySet_SendsNeitherHeader()
    {
        var client = BrowserClient(TrustedClientContract.BrowserIpv4);
        await SignInAsync(factory, client, UserRole);

        var login = Assert.Single(factory.Api.Requests, request => request.Path == ApiLogin);
        Assert.False(TrustedClientContract.CarriesEither(login));
    }

    // Every API call an admin's pages make, other than register, carries neither header
    [Fact]
    public async Task AdminPages_KeySet_SendNeitherHeader()
    {
        var client = BrowserClient(TrustedClientContract.BrowserIpv4);
        factory.Api.Answer(HttpMethod.Get, ApiRegistrationRequests, HttpStatusCode.OK, RegistrationSpec.RequestsBody());
        factory.Api.Answer(HttpMethod.Get, ApiAccounts, HttpStatusCode.OK, "[]");
        factory.Api.Answer(HttpMethod.Get, ApiAuditRecords, HttpStatusCode.OK, "[]");
        await SignInAsync(factory, client, AdminRole);

        foreach (var path in new[] { HomePath, AdminPath, UsersPath, AuditPath, RegistrationRequestsPath })
            await client.GetAsync(path);

        Assert.True(factory.Api.Requests.Count > 1, "The pages made no API call besides the login.");
        Assert.All(factory.Api.Requests, request => Assert.False(TrustedClientContract.CarriesEither(request), $"{request.Method} {request.Path} carried a trusted client header."));
    }

    // --- AC5: the key reaches neither the logs nor a page ---

    [Theory]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task RegisterPage_AnyApiAnswer_NeverShowsOrLogsTheKey(HttpStatusCode statusCode)
    {
        var client = BrowserClient(TrustedClientContract.BrowserIpv4);
        var body = statusCode == HttpStatusCode.Accepted
            ? RegistrationSpec.RegistrationRequestCreatedBody(Guid.NewGuid())
            : RegistrationSpec.ErrorBody(RegistrationSpec.EmailInUseCode, RegistrationSpec.EmailInUseDescription);
        factory.Api.Answer(HttpMethod.Post, ApiRegister, statusCode, body);

        var response = await TrustedClientContract.RegisterThroughThePageAsync(client);

        Assert.DoesNotContain(ClientKeyWebAppFactory.Key, $"{response.Headers} {await response.Content.ReadAsStringAsync()}");
        Assert.NotEmpty(factory.Logs.Lines);
        Assert.False(factory.Logs.Contain(ClientKeyWebAppFactory.Key), "The client key was logged.");
    }

    // --- helpers ---

    private HttpClient BrowserClient(string browserAddress)
    {
        var client = CreateClient(factory);
        BrowserAddress.Set(client, browserAddress);
        return client;
    }

    private void AnswerRegister() =>
        factory.Api.Answer(HttpMethod.Post, ApiRegister, HttpStatusCode.Accepted, RegistrationSpec.RegistrationRequestCreatedBody(Guid.NewGuid()));

    // The WebApp request being served, as IHttpContextAccessor gives it to the client
    private void ServeBrowser(IPAddress? address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = address;
        httpContextAccessor.HttpContext = context;
    }

    private async Task<RecordedApiRequest> RegisterThroughTheClientAsync()
    {
        AnswerRegister();
        await authApi.RegisterAsync(TrustedClientContract.Email, TrustedClientContract.Password, CancellationToken.None);
        return Assert.Single(factory.Api.Requests, request => request.Path == ApiRegister);
    }
}

// #172 PR 4, AC4 with Api:ClientKey empty: the WebApp sends neither header, even for a known browser address
public class TrustedClientHeadersWithoutKeyTests : IClassFixture<StubbedApiWebAppFactory>, IDisposable
{
    private readonly StubbedApiWebAppFactory factory;
    private readonly IServiceScope scope;
    private readonly IHttpContextAccessor httpContextAccessor;

    public TrustedClientHeadersWithoutKeyTests(StubbedApiWebAppFactory factory)
    {
        this.factory = factory;
        factory.Api.Reset();
        scope = factory.Services.CreateScope();
        httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
    }

    public void Dispose()
    {
        httpContextAccessor.HttpContext = null;
        scope.Dispose();
    }

    [Fact]
    public async Task RegisterAsync_NoKey_SendsNeitherHeader()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(TrustedClientContract.BrowserIpv4);
        httpContextAccessor.HttpContext = context;
        factory.Api.Answer(HttpMethod.Post, ApiRegister, HttpStatusCode.Accepted, RegistrationSpec.RegistrationRequestCreatedBody(Guid.NewGuid()));

        await scope.ServiceProvider.GetRequiredService<IAuthApiClient>()
            .RegisterAsync(TrustedClientContract.Email, TrustedClientContract.Password, CancellationToken.None);

        Assert.False(TrustedClientContract.CarriesEither(Assert.Single(factory.Api.Requests)));
    }

    [Fact]
    public async Task RegisterPage_NoKey_SendsNeitherHeader()
    {
        var client = CreateClient(factory);
        BrowserAddress.Set(client, TrustedClientContract.BrowserIpv4);
        factory.Api.Answer(HttpMethod.Post, ApiRegister, HttpStatusCode.Accepted, RegistrationSpec.RegistrationRequestCreatedBody(Guid.NewGuid()));

        await TrustedClientContract.RegisterThroughThePageAsync(client);

        var request = Assert.Single(factory.Api.Requests, request => request.Path == ApiRegister);
        Assert.False(TrustedClientContract.CarriesEither(request));
    }
}

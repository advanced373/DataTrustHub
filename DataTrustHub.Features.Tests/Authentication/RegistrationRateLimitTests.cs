using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Registration;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

// #172 AC6 and AC7 through the API, each test on its own host (the rate limiter's windows live in the host):
// the (PermitsPerHourPerIp + 1)-th register from one client address within the hour gets 429; another address
// is not affected; other endpoints are not affected by the Registration policy. The client address is set per
// request by RegistrationApiFactory (TestServer has none)
public sealed class RegistrationRateLimitTests
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string Password = "Password1!";
    private const string FirstAddress = "203.0.113.10";
    private const string SecondAddress = "203.0.113.20";
    private const string Ipv6Address = "2001:db8::1";
    private const int ConfiguredPermits = 2;

    // --- AC6: with the committed limit (appsettings.json) ---

    [Fact]
    public async Task Register_WithinTheLimitFromOneAddress_IsNeverRateLimited()
    {
        await using var host = await HostAsync(permits: null);

        var statuses = await RegisterManyAsync(host, FirstAddress, RegistrationOptions.DefaultPermitsPerHourPerIp);

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Accepted, status));
    }

    [Fact]
    public async Task Register_OneOverTheLimitFromOneAddress_Returns429()
    {
        await using var host = await HostAsync(permits: null);
        await RegisterManyAsync(host, FirstAddress, RegistrationOptions.DefaultPermitsPerHourPerIp);

        var response = await RegisterAsync(host, FirstAddress, NewEmail());

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    // Refused registrations (409, 400) use permits too: the limit counts calls, not accepted requests
    [Fact]
    public async Task Register_RefusedCallsFromOneAddress_CountTowardsTheLimit()
    {
        await using var host = await HostAsync(permits: null);
        var email = NewEmail();
        for (var i = 0; i < RegistrationOptions.DefaultPermitsPerHourPerIp; i++) await RegisterAsync(host, FirstAddress, email);

        var response = await RegisterAsync(host, FirstAddress, NewEmail());

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Register_OverTheLimitFromOneAddress_AddsNoRequest()
    {
        await using var host = await HostAsync(permits: null);
        await RegisterManyAsync(host, FirstAddress, RegistrationOptions.DefaultPermitsPerHourPerIp);
        var email = NewEmail();

        await RegisterAsync(host, FirstAddress, email);

        using var scope = host.Factory.Services.CreateScope();
        var normalized = EmailAddress.From(email).Value;
        Assert.False(await scope.ServiceProvider.GetRequiredService<DContext>().RegistrationRequests.AnyAsync(r => r.Email == normalized));
    }

    [Theory]
    [InlineData(SecondAddress)]
    [InlineData(Ipv6Address)]
    public async Task Register_AnotherAddressAfterOneReachedTheLimit_Returns202(string otherAddress)
    {
        await using var host = await HostAsync(permits: null);
        await RegisterManyAsync(host, FirstAddress, RegistrationOptions.DefaultPermitsPerHourPerIp + 1);

        var response = await RegisterAsync(host, otherAddress, NewEmail());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    // Requests without a client address share one partition, so they are limited together
    [Fact]
    public async Task Register_WithoutAClientAddress_SharesOneLimit()
    {
        await using var host = await HostAsync(permits: null);
        await RegisterManyAsync(host, address: null, RegistrationOptions.DefaultPermitsPerHourPerIp);

        var response = await RegisterAsync(host, address: null, NewEmail());

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Login_FromAnAddressThatReachedTheRegisterLimit_IsNotRateLimited()
    {
        await using var host = await HostAsync(permits: null);
        await RegisterManyAsync(host, FirstAddress, RegistrationOptions.DefaultPermitsPerHourPerIp + 1);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i <= RegistrationOptions.DefaultPermitsPerHourPerIp; i++)
            statuses.Add((await SendAsync(host, LoginEndpoint, FirstAddress, NewEmail())).StatusCode);

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Unauthorized, status));
    }

    // --- AC7: the limit is read from configuration ---

    [Fact]
    public async Task Register_ConfiguredPermits_LimitAtThatNumber()
    {
        await using var host = await HostAsync(permits: ConfiguredPermits);

        var statuses = await RegisterManyAsync(host, FirstAddress, ConfiguredPermits + 1);

        Assert.Equal(
            [.. Enumerable.Repeat(HttpStatusCode.Accepted, ConfiguredPermits), HttpStatusCode.TooManyRequests],
            statuses);
    }

    // --- helpers ---

    private static string NewEmail() => $"limit_{Guid.NewGuid():N}@example.com";

    // permits null: the value of appsettings.json
    private static async Task<OwnedHost> HostAsync(int? permits) =>
        new(await RegistrationApiFactory.StartAsync(new Dictionary<string, string?>
        {
            [RegistrationApiFactory.PermitsPerHourPerIpSetting] = permits?.ToString()
        }));

    private static async Task<List<HttpStatusCode>> RegisterManyAsync(OwnedHost host, string? address, int count)
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < count; i++) statuses.Add((await RegisterAsync(host, address, NewEmail())).StatusCode);
        return statuses;
    }

    private static Task<HttpResponseMessage> RegisterAsync(OwnedHost host, string? address, string email) =>
        SendAsync(host, RegisterEndpoint, address, email);

    private static Task<HttpResponseMessage> SendAsync(OwnedHost host, string endpoint, string? address, string email)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(new { Email = email, Password }) };
        if (address is not null) request.Headers.Add(RegistrationApiFactory.ClientAddressHeader, address);
        return host.Client.SendAsync(request);
    }

    private sealed class OwnedHost : IAsyncDisposable
    {
        public OwnedHost(RegistrationApiFactory factory)
        {
            Factory = factory;
            Client = factory.CreateClient();
        }

        public RegistrationApiFactory Factory { get; }

        public HttpClient Client { get; }

        public async ValueTask DisposeAsync() => await Factory.StopAsync();
    }
}

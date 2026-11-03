using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

// #172 PR 4 (trusted client address), AC1, AC2, AC3 and the API side of AC5, each test on its own host (the rate
// limiter's windows live in the host). The WebApp calls the API from its own server address and sends the
// browser's address with the shared key; the API partitions register by that address only when the key matches.
// The connection address is named per request with RegistrationApiFactory.ClientAddressHeader (TestServer has none)
public sealed class TrustedClientAddressRateLimitTests
{
    private const string RegisterEndpoint = "/auth/register";
    private const string Password = "Password1!";
    private const int Permits = 2;

    // A 44-character key, like the base64 key infra/setup.ps1 generates (at least 32 characters)
    private const string TrustedKey = "dGhpcy1pcy1hLXRlc3QtY2xpZW50LWtleS0xMjM0NTY=";

    // The WebApp server, as the API sees its connection, and a direct caller
    private const string WebAppServerAddress = "198.51.100.7";
    private const string DirectCallerAddress = "198.51.100.66";

    // Browser addresses the WebApp passes on
    private const string FirstBrowserAddress = "203.0.113.10";
    private const string SecondBrowserAddress = "203.0.113.20";
    private const string Ipv6BrowserAddress = "2001:db8::42";

    // --- AC1: the right key and a valid address: partitioned by the header address ---

    [Theory]
    [InlineData(SecondBrowserAddress)]
    [InlineData(Ipv6BrowserAddress)]
    public async Task Register_TrustedHeaderAddresses_EachGetTheirOwnBudget(string otherBrowserAddress)
    {
        await using var host = await HostAsync(TrustedKey);
        var first = await RegisterManyAsync(host, Permits + 1, _ => Trusted(FirstBrowserAddress));

        var other = await RegisterManyAsync(host, Permits, _ => Trusted(otherBrowserAddress));

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.Accepted, Permits), HttpStatusCode.TooManyRequests], first);
        Assert.All(other, status => Assert.Equal(HttpStatusCode.Accepted, status));
    }

    [Fact]
    public async Task Register_TrustedHeaderAddress_IsLimitedAtPermitsPerHourPerIp()
    {
        await using var host = await HostAsync(TrustedKey);

        var statuses = await RegisterManyAsync(host, Permits + 1, _ => Trusted(FirstBrowserAddress));

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.Accepted, Permits), HttpStatusCode.TooManyRequests], statuses);
    }

    // Many browsers through one WebApp server never use up the server's own connection budget
    [Fact]
    public async Task Register_ManyTrustedBrowsersThroughOneServer_AreNotLimitedTogether()
    {
        await using var host = await HostAsync(TrustedKey);

        var statuses = await RegisterManyAsync(host, Permits * 3, i => Trusted($"203.0.113.{100 + i}"));

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Accepted, status));
    }

    // The header address is the partition, not the connection: the server's untrusted calls keep their budget
    [Fact]
    public async Task Register_TrustedBrowserAtItsLimit_LeavesTheConnectionBudgetUntouched()
    {
        await using var host = await HostAsync(TrustedKey);
        await RegisterManyAsync(host, Permits + 1, _ => Trusted(FirstBrowserAddress));

        var response = await RegisterAsync(host, WebAppServerAddress, NoHeaders);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    // --- AC2: no configured key, a missing or wrong key, a malformed address: the connection address ---

    // The faulty header sets of AC2, all from the WebApp server's connection. Each call names another browser
    // address, so they share one partition only if the headers are ignored
    public static TheoryData<string, string> UntrustedHeaders => new()
    {
        { nameof(WrongKeySameLength), "" },
        { nameof(WrongKeyCase), "" },
        { nameof(KeyPrefix), "" },
        { nameof(KeyWithSuffix), "" },
        { nameof(EmptyKey), "" },
        { nameof(MissingKey), "" },
        { nameof(DuplicatedKey), "" },
        { nameof(DuplicatedAddress), "" },
        { nameof(MalformedAddress), "not-an-ip-address" },
        { nameof(MalformedAddress), "203.0.113.300" },
        { nameof(MalformedAddress), "203.0.113.10, 203.0.113.11" },
        { nameof(MalformedAddress), "" },
        { nameof(MissingAddress), "" },
    };

    [Theory]
    [MemberData(nameof(UntrustedHeaders))]
    public async Task Register_UntrustedHeaders_UseTheConnectionAddress(string headerSet, string malformedAddress)
    {
        await using var host = await HostAsync(TrustedKey);

        var statuses = await RegisterManyAsync(host, Permits + 1, i => Untrusted(headerSet, i, malformedAddress));

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.Accepted, Permits), HttpStatusCode.TooManyRequests], statuses);
    }

    // After the untrusted calls used the connection's budget, a plain call from that connection is limited too
    [Theory]
    [MemberData(nameof(UntrustedHeaders))]
    public async Task Register_UntrustedHeaders_UseUpTheConnectionBudget(string headerSet, string malformedAddress)
    {
        await using var host = await HostAsync(TrustedKey);
        await RegisterManyAsync(host, Permits, i => Untrusted(headerSet, i, malformedAddress));

        var response = await RegisterAsync(host, WebAppServerAddress, NoHeaders);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    // Without a configured key no header is trusted, not even one that carries a key (or an empty one)
    [Theory]
    [InlineData(null, TrustedKey)]
    [InlineData(null, "")]
    [InlineData("", "")]
    public async Task Register_NoKeyConfigured_UsesTheConnectionAddress(string? configuredKey, string sentKey)
    {
        await using var host = await HostAsync(configuredKey);

        var statuses = await RegisterManyAsync(host, Permits + 1, i => new Call(WebAppServerAddress, BrowserAddress(i), [sentKey]));

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.Accepted, Permits), HttpStatusCode.TooManyRequests], statuses);
    }

    // With no connection address either, untrusted calls share the one no-address partition
    [Fact]
    public async Task Register_WrongKeyAndNoConnectionAddress_ShareOneLimit()
    {
        await using var host = await HostAsync(TrustedKey);

        var statuses = await RegisterManyAsync(host, Permits + 1, i => new Call(null, BrowserAddress(i), [WrongKeySameLength]));

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.Accepted, Permits), HttpStatusCode.TooManyRequests], statuses);
    }

    // --- AC3: a direct caller who sends only the address header cannot change its partition ---

    [Fact]
    public async Task Register_DirectCallerWithOnlyAnAddressHeader_UsesUpItsOwnBudget()
    {
        await using var host = await HostAsync(TrustedKey);

        var statuses = await RegisterManyAsync(host, Permits + 1, i => new Call(DirectCallerAddress, BrowserAddress(i), null));

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.Accepted, Permits), HttpStatusCode.TooManyRequests], statuses);
    }

    // A direct caller that names a browser address does not use that browser's budget
    [Fact]
    public async Task Register_DirectCallerNamingABrowserAddress_LeavesThatBrowsersBudgetUntouched()
    {
        await using var host = await HostAsync(TrustedKey);
        await RegisterManyAsync(host, Permits + 1, _ => new Call(DirectCallerAddress, FirstBrowserAddress, null));

        var browser = await RegisterManyAsync(host, Permits, _ => Trusted(FirstBrowserAddress));

        Assert.All(browser, status => Assert.Equal(HttpStatusCode.Accepted, status));
    }

    // Nor can it escape its own limit by naming a browser whose budget is left
    [Fact]
    public async Task Register_DirectCallerAtItsLimit_IsStillLimitedWhenNamingAnotherAddress()
    {
        await using var host = await HostAsync(TrustedKey);
        await RegisterManyAsync(host, Permits, _ => new Call(DirectCallerAddress, (string?)null, null));

        var response = await RegisterAsync(host, DirectCallerAddress, new Headers(SecondBrowserAddress, null));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    // --- AC5: the key never reaches the logs or a response ---

    [Fact]
    public async Task Register_TrustedAndUntrustedCalls_NeverLogTheKey()
    {
        await using var host = await HostAsync(TrustedKey);
        await RegisterManyAsync(host, Permits + 1, _ => Trusted(FirstBrowserAddress));
        await RegisterManyAsync(host, Permits + 1, i => Untrusted(nameof(KeyPrefix), i, ""));

        Assert.NotEmpty(host.Factory.Logs.Lines);
        Assert.False(host.Factory.Logs.Contain(TrustedKey), "The trusted client key was logged.");
        Assert.False(host.Factory.Logs.Contain(KeyPrefix), "A sent client key was logged.");
    }

    [Fact]
    public async Task Register_Responses_NeverCarryTheKey()
    {
        await using var host = await HostAsync(TrustedKey);
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i <= Permits; i++) responses.Add(await RegisterAsync(host, WebAppServerAddress, Trusted(FirstBrowserAddress).Headers));
        responses.Add(await RegisterAsync(host, WebAppServerAddress, Trusted(SecondBrowserAddress).Headers, email: "not-an-email"));
        for (var i = 0; i <= Permits; i++) responses.Add(await RegisterAsync(host, WebAppServerAddress, Untrusted(nameof(WrongKeySameLength), i, "").Headers));

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
        foreach (var response in responses)
            Assert.DoesNotContain(TrustedKey, await DescribeAsync(response));
    }

    // --- the faulty header sets (AC2) ---

    // Same length as TrustedKey, last character changed
    private const string WrongKeySameLength = "dGhpcy1pcy1hLXRlc3QtY2xpZW50LWtleS0xMjM0NTZ=";
    private static readonly string WrongKeyCase = TrustedKey.ToUpperInvariant();
    private static readonly string KeyPrefix = TrustedKey[..^1];
    private static readonly string KeyWithSuffix = TrustedKey + "A";
    private const string EmptyKey = "";
    private const string MissingKey = "missing";
    private const string DuplicatedKey = "duplicated key";
    private const string DuplicatedAddress = "duplicated address";
    private const string MalformedAddress = "malformed address";
    private const string MissingAddress = "missing address";

    private static Call Untrusted(string headerSet, int index, string malformedAddress)
    {
        var address = BrowserAddress(index);
        return headerSet switch
        {
            nameof(WrongKeySameLength) => new Call(WebAppServerAddress, address, [WrongKeySameLength]),
            nameof(WrongKeyCase) => new Call(WebAppServerAddress, address, [WrongKeyCase]),
            nameof(KeyPrefix) => new Call(WebAppServerAddress, address, [KeyPrefix]),
            nameof(KeyWithSuffix) => new Call(WebAppServerAddress, address, [KeyWithSuffix]),
            nameof(EmptyKey) => new Call(WebAppServerAddress, address, [EmptyKey]),
            nameof(MissingKey) => new Call(WebAppServerAddress, address, null),
            nameof(DuplicatedKey) => new Call(WebAppServerAddress, address, [TrustedKey, TrustedKey]),
            nameof(DuplicatedAddress) => new Call(WebAppServerAddress, [address, BrowserAddress(index + 50)], [TrustedKey]),
            nameof(MalformedAddress) => new Call(WebAppServerAddress, malformedAddress, [TrustedKey]),
            nameof(MissingAddress) => new Call(WebAppServerAddress, (string[]?)null, [TrustedKey]),
            _ => throw new ArgumentOutOfRangeException(nameof(headerSet), headerSet, null)
        };
    }

    // --- helpers ---

    private static readonly Headers NoHeaders = new((string?)null, null);

    // Headers: the X-DTH-Client-Address values and the X-DTH-Client-Key values (null: header not sent)
    private sealed record Headers(string[]? Addresses, string[]? Keys)
    {
        public Headers(string? address, string[]? keys) : this(address is null ? null : [address], keys)
        {
        }
    }

    // One register call: the connection address (null: none) and the headers
    private sealed record Call(string? ConnectionAddress, Headers Headers)
    {
        public Call(string? connectionAddress, string? address, string[]? keys) : this(connectionAddress, new Headers(address, keys))
        {
        }

        public Call(string? connectionAddress, string[]? addresses, string[]? keys) : this(connectionAddress, new Headers(addresses, keys))
        {
        }
    }

    // The WebApp's call for a browser: from the WebApp server, with the right key
    private static Call Trusted(string browserAddress) => new(WebAppServerAddress, browserAddress, [TrustedKey]);

    private static string BrowserAddress(int index) => $"203.0.113.{index + 1}";

    private static string NewEmail() => $"trusted_{Guid.NewGuid():N}@example.com";

    private static async Task<OwnedHost> HostAsync(string? trustedClientKey) =>
        new(await TrustedClientApiFactory.StartAsync(trustedClientKey, Permits));

    private static async Task<List<HttpStatusCode>> RegisterManyAsync(OwnedHost host, int count, Func<int, Call> call)
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < count; i++)
        {
            var next = call(i);
            statuses.Add((await RegisterAsync(host, next.ConnectionAddress, next.Headers)).StatusCode);
        }
        return statuses;
    }

    private static Task<HttpResponseMessage> RegisterAsync(OwnedHost host, string? connectionAddress, Headers headers, string? email = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RegisterEndpoint)
        {
            Content = JsonContent.Create(new { Email = email ?? NewEmail(), Password })
        };
        if (connectionAddress is not null) request.Headers.Add(RegistrationApiFactory.ClientAddressHeader, connectionAddress);
        if (headers.Addresses is not null) request.Headers.TryAddWithoutValidation(TrustedClientApiFactory.TrustedAddressHeader, headers.Addresses);
        if (headers.Keys is not null) request.Headers.TryAddWithoutValidation(TrustedClientApiFactory.TrustedKeyHeader, headers.Keys);
        return host.Client.SendAsync(request);
    }

    // Everything a caller sees of a response: status, headers and body
    private static async Task<string> DescribeAsync(HttpResponseMessage response) =>
        $"{response.StatusCode} {response.Headers} {response.Content.Headers} {await response.Content.ReadAsStringAsync()}";

    private sealed class OwnedHost : IAsyncDisposable
    {
        public OwnedHost(TrustedClientApiFactory factory)
        {
            Factory = factory;
            Client = factory.CreateClient();
        }

        public TrustedClientApiFactory Factory { get; }

        public HttpClient Client { get; }

        public async ValueTask DisposeAsync() => await Factory.StopAsync();
    }
}

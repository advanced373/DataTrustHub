using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

// #172 AC4, AC5 and AC7 through the API, each test on its own host (its own SQLite database and clock), with
// the limits set in configuration: requests older than PendingRequestLifetimeDays are deleted when anyone
// registers, after which their email can register again; at MaxPendingRequests pending requests register
// answers 503 Registration.QueueFull and adds nothing, and expired requests do not count
public sealed class RegistrationPurgeAndQueueTests : IAsyncLifetime
{
    private const string RegisterEndpoint = "/auth/register";
    private const string Password = "Password1!";
    private const string RegisterUserAction = "RegisterUser";
    private const string RejectedOutcome = "Rejected";
    private const int LifetimeDays = 2;
    private const int MaxPendingRequests = 3;

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(LifetimeDays);
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    private RegistrationApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = await RegistrationApiFactory.StartAsync(new Dictionary<string, string?>
        {
            [RegistrationApiFactory.LifetimeDaysSetting] = LifetimeDays.ToString(),
            [RegistrationApiFactory.MaxPendingRequestsSetting] = MaxPendingRequests.ToString()
        });
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync() => _factory.StopAsync();

    // --- AC4: the purge of expired requests ---

    [Fact]
    public async Task Register_AfterTheLifetime_DeletesTheExpiredRequest()
    {
        await RegisterAsync("expired@example.com");
        _factory.Clock.Advance(Lifetime + OneSecond);

        await RegisterAsync("someone.else@example.com");

        Assert.False(await RequestExistsAsync("expired@example.com"));
    }

    [Fact]
    public async Task Register_AfterTheLifetime_KeepsTheRequestsThatAreStillPending()
    {
        await RegisterAsync("old@example.com");
        _factory.Clock.Advance(Lifetime);
        await RegisterAsync("recent@example.com");
        _factory.Clock.Advance(OneSecond);

        await RegisterAsync("trigger@example.com");

        Assert.Equal((false, true), (await RequestExistsAsync("old@example.com"), await RequestExistsAsync("recent@example.com")));
    }

    // A refused register purges too: "when anyone registers"
    [Fact]
    public async Task Register_RefusedBecauseTheEmailIsPending_StillDeletesExpiredRequests()
    {
        await RegisterAsync("expiring@example.com");
        _factory.Clock.Advance(Lifetime - OneSecond);
        await RegisterAsync("blocker@example.com");
        _factory.Clock.Advance(OneSecond * 2);

        var refused = await RegisterAsync("blocker@example.com");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.False(await RequestExistsAsync("expiring@example.com"));
    }

    [Fact]
    public async Task Register_SameEmailAfterItsRequestExpired_Returns202WithANewRequest()
    {
        var first = await RegisterAndReadIdAsync("again@example.com");
        _factory.Clock.Advance(Lifetime + OneSecond);

        var response = await RegisterAsync(" AGAIN@example.com ");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var second = (await response.Content.ReadFromJsonAsync<RegisterUserResponse>())!.RegistrationRequestId;
        Assert.NotEqual(first, second);
        Assert.Equal(second, (await StoredRequestIdsAsync("again@example.com")).Single());
    }

    // Exactly the lifetime old is not older than the lifetime: the request is still pending
    [Fact]
    public async Task Register_SameEmailExactlyAtTheLifetime_Returns409AlreadyPending()
    {
        await RegisterAsync("boundary@example.com");
        _factory.Clock.Advance(Lifetime);

        var response = await RegisterAsync("boundary@example.com");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AuthErrorCodes.RegistrationAlreadyPending, AuthErrorMessages.RegistrationAlreadyPending);
    }

    // --- AC5: the cap on pending requests ---

    [Fact]
    public async Task Register_WithMaxPendingRequests_Returns503QueueFull()
    {
        await FillTheQueueAsync();

        var response = await RegisterAsync("over.the.cap@example.com");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.ServiceUnavailable,
            AuthErrorCodes.RegistrationQueueFull, AuthErrorMessages.RegistrationQueueFull);
    }

    [Fact]
    public async Task Register_WithMaxPendingRequests_AddsNothing()
    {
        await FillTheQueueAsync();

        await RegisterAsync("over.the.cap@example.com");

        Assert.Equal(MaxPendingRequests, await RequestCountAsync());
        Assert.False(await RequestExistsAsync("over.the.cap@example.com"));
    }

    [Fact]
    public async Task Register_OneBelowTheCap_Returns202()
    {
        for (var i = 0; i < MaxPendingRequests - 1; i++) await RegisterAsync($"below{i}@example.com");

        var response = await RegisterAsync("last.place@example.com");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Register_QueueFullOfExpiredRequests_Returns202()
    {
        await FillTheQueueAsync();
        _factory.Clock.Advance(Lifetime + OneSecond);

        var response = await RegisterAsync("after.expiry@example.com");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, await RequestCountAsync());
    }

    // Only expired requests are not counted: one expired plus the rest pending leaves room for one
    [Fact]
    public async Task Register_QueueFullWithOneExpiredRequest_AcceptsOneMoreOnly()
    {
        await RegisterAsync("first.in@example.com");
        _factory.Clock.Advance(Lifetime - OneSecond);
        for (var i = 1; i < MaxPendingRequests; i++) await RegisterAsync($"later{i}@example.com");
        _factory.Clock.Advance(OneSecond * 2);

        var accepted = await RegisterAsync("takes.the.place@example.com");
        var refused = await RegisterAsync("one.too.many@example.com");

        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.ServiceUnavailable), (accepted.StatusCode, refused.StatusCode));
    }

    // AC8: the refusal is audited as RegisterUser with its outcome and code
    [Fact]
    public async Task Register_WithMaxPendingRequests_IsAuditedAsRejectedWithQueueFull()
    {
        await FillTheQueueAsync();

        await RegisterAsync("audited.over.the.cap@example.com");

        using var scope = _factory.Services.CreateScope();
        var refusals = await scope.ServiceProvider.GetRequiredService<DContext>().AuditRecords.AsNoTracking()
            .Where(r => r.Action == RegisterUserAction && r.Outcome == RejectedOutcome).ToListAsync();
        Assert.Equal(AuthErrorCodes.RegistrationQueueFull, Assert.Single(refusals).ErrorCode);
    }

    // A full queue does not hide an email that is already taken (the email checks come first)
    [Fact]
    public async Task Register_QueueFullAndEmailPending_Returns409AlreadyPending()
    {
        await FillTheQueueAsync();

        var response = await RegisterAsync("queued0@example.com");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- helpers ---

    private async Task FillTheQueueAsync()
    {
        for (var i = 0; i < MaxPendingRequests; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await RegisterAsync($"queued{i}@example.com")).StatusCode);
    }

    private Task<HttpResponseMessage> RegisterAsync(string email) =>
        _client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });

    private async Task<Guid> RegisterAndReadIdAsync(string email)
    {
        var response = await RegisterAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RegisterUserResponse>())!.RegistrationRequestId;
    }

    private async Task<bool> RequestExistsAsync(string email) => (await StoredRequestIdsAsync(email)).Count > 0;

    private async Task<List<Guid>> StoredRequestIdsAsync(string email)
    {
        var normalized = EmailAddress.From(email).Value;
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().RegistrationRequests
            .Where(r => r.Email == normalized).Select(r => r.Id).ToListAsync();
    }

    private async Task<int> RequestCountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().RegistrationRequests.CountAsync();
    }

    private record RegisterUserResponse(Guid RegistrationRequestId);
}

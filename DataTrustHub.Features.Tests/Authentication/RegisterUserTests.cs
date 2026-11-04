using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

// #172 AC1, AC2, AC3 and AC8 through the API: register submits a registration request (202 with its id), stores
// the normalized email and a password hash and adds no user; logging in with a pending request's credentials
// answers like an unknown account; an email with an account or a pending request is refused with 409; invalid
// input still gets 400
public class RegisterUserTests(RegistrationApiFactory factory) : IClassFixture<RegistrationApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string Password = "Password1!";
    private const string EmailDomain = "@example.com";

    private readonly HttpClient _client = factory.CreateClient();

    // --- AC1: a registration request, not an account ---

    [Fact]
    public async Task RegisterUser_WithValidData_Returns202WithRegistrationRequestId()
    {
        var response = await RegisterAsync(NewEmail("valid"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RegisterUserResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.RegistrationRequestId);
    }

    [Fact]
    public async Task RegisterUser_WithValidData_AddsNoUser()
    {
        var email = NewEmail("no_user");

        await RegisterAsync(email);

        Assert.False(await UserExistsAsync(email));
    }

    [Fact]
    public async Task RegisterUser_WithValidData_StoresOneRequestWithItsIdAndTheNormalizedEmail()
    {
        var email = NewEmail("stored");

        var requestId = await RegisterAndReadIdAsync($" {email.ToUpperInvariant()}\t");

        var stored = Assert.Single(await StoredRequestsAsync(email));
        Assert.Equal((requestId, email), (stored.Id, stored.Email));
    }

    [Fact]
    public async Task RegisterUser_WithValidData_StoresTheRequestTimeFromTheClock()
    {
        var email = NewEmail("requested_at");

        await RegisterAsync(email);

        Assert.Equal(factory.Clock.UtcNow, Assert.Single(await StoredRequestsAsync(email)).RequestedAt);
    }

    // The plain password is never stored: the column holds a hash that the host's password hasher accepts
    [Fact]
    public async Task RegisterUser_WithValidData_StoresAPasswordHashNeverThePlainPassword()
    {
        var email = NewEmail("hashed");

        await RegisterAsync(email);

        var stored = Assert.Single(await StoredRequestsAsync(email));
        Assert.DoesNotContain(Password, stored.HashedPassword);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(PasswordVerificationResult.Success, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().VerifyHashedPassword(stored.HashedPassword, Password));
    }

    // --- AC2: a pending request cannot log in ---

    [Fact]
    public async Task LoginUser_WithThePendingRequestsCredentials_AnswersExactlyLikeAnUnknownAccount()
    {
        var email = NewEmail("pending_login");
        await RegisterAsync(email);

        var pending = await LoginAsync(email);
        var unknown = await LoginAsync(NewEmail("pending_login_ghost"));

        Assert.Equal(HttpStatusCode.Unauthorized, pending.StatusCode);
        Assert.Equal(unknown.StatusCode, pending.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await pending.Content.ReadAsStringAsync());
    }

    // --- AC3: one account and one pending request per email ---

    [Fact]
    public async Task RegisterUser_EmailWithAnAccount_Returns409EmailAlreadyInUse()
    {
        var email = NewEmail("taken");
        await TestUsers.CreateAsync(factory, email);

        var response = await RegisterAsync(email);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AuthErrorCodes.EmailAlreadyInUse, AuthErrorMessages.EmailAlreadyInUse);
    }

    [Fact]
    public async Task RegisterUser_EmailWithAnAccount_AddsNoRequest()
    {
        var email = NewEmail("taken_no_request");
        await TestUsers.CreateAsync(factory, email);

        await RegisterAsync(email);

        Assert.Empty(await StoredRequestsAsync(email));
    }

    [Fact]
    public async Task RegisterUser_EmailWithAPendingRequest_Returns409AlreadyPending()
    {
        var email = NewEmail("duplicate");
        await RegisterAsync(email);

        var response = await RegisterAsync(email);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AuthErrorCodes.RegistrationAlreadyPending, AuthErrorMessages.RegistrationAlreadyPending);
    }

    [Fact]
    public async Task RegisterUser_EmailWithAPendingRequest_KeepsTheFirstRequestOnly()
    {
        var email = NewEmail("duplicate_kept");
        var firstId = await RegisterAndReadIdAsync(email);

        await RegisterAsync(email);

        Assert.Equal(firstId, Assert.Single(await StoredRequestsAsync(email)).Id);
    }

    [Theory]
    [InlineData("reg_pending_case@example.com", "REG_Pending_Case@Example.COM")]
    [InlineData("reg_pending_space@example.com", " \treg_pending_space@example.com ")]
    [InlineData(" Reg_Pending_Reverse@Example.com ", "reg_pending_reverse@example.com")]
    public async Task RegisterUser_CaseOrWhitespaceVariantOfAPendingEmail_Returns409AlreadyPending(string pending, string variant)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await RegisterAsync(pending)).StatusCode);

        var response = await RegisterAsync(variant);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AuthErrorCodes.RegistrationAlreadyPending, AuthErrorMessages.RegistrationAlreadyPending);
    }

    // --- AC8: validation unchanged ---

    [Fact]
    public async Task RegisterUser_WithInvalidEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync(RegisterEndpoint, new { Email = "not-an-email", Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterUser_WithShortPassword_Returns400()
    {
        var response = await _client.PostAsJsonAsync(RegisterEndpoint, new { Email = NewEmail("short"), Password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterUser_WithInvalidInput_AddsNoRequest()
    {
        var email = NewEmail("invalid_no_request");

        await _client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password = "short" });

        Assert.Empty(await StoredRequestsAsync(email));
    }

    // --- helpers ---

    private static string NewEmail(string prefix) => $"reg_{prefix}_{Guid.NewGuid():N}{EmailDomain}";

    private Task<HttpResponseMessage> RegisterAsync(string email) =>
        _client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });

    private Task<HttpResponseMessage> LoginAsync(string email) =>
        _client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password });

    private async Task<Guid> RegisterAndReadIdAsync(string email)
    {
        var response = await RegisterAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RegisterUserResponse>())!.RegistrationRequestId;
    }

    private async Task<List<DbRegistrationRequest>> StoredRequestsAsync(string email)
    {
        var normalized = EmailAddress.From(email).Value;
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().RegistrationRequests.AsNoTracking()
            .Where(r => r.Email == normalized).ToListAsync();
    }

    private async Task<bool> UserExistsAsync(string email)
    {
        var normalized = EmailAddress.From(email).Value;
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().Users.AnyAsync(u => u.Email == normalized);
    }

    private record RegisterUserResponse(Guid RegistrationRequestId);
}

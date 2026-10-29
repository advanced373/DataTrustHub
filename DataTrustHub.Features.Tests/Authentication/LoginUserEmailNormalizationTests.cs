using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

// AC4 and AC7 of #123: a user signs in with any case or space variant of their address, the token carries
// the normalized email (U7), and an unknown address and a wrong password still fail the same way (U4)
public class LoginUserEmailNormalizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string Password = "Password1!";
    private const string WrongPassword = "WrongPassword1!";
    private const string RegisteredEmail = "login_variant@example.com";

    private readonly HttpClient _client = factory.CreateClient();

    // The variants themselves are pinned on EmailAddress (EmailAddressTests) and on the handler
    // (AuthenticationHandlerTests)
    [Fact]
    public async Task LoginUser_CaseOrSpaceVariantOfTheEmail_Returns200WithToken()
    {
        await RegisterAsync(RegisteredEmail);

        var response = await LoginAsync("\tLogin_Variant@Example.Com\n", Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrEmpty((await response.Content.ReadFromJsonAsync<LoginUserResponse>())!.Token));
    }

    [Fact]
    public async Task LoginUser_RegisteredWithAVariant_SignsInWithTheNormalizedEmail()
    {
        await RegisterAsync(" Login.Registered.Variant@Example.COM ");

        var response = await LoginAsync("login.registered.variant@example.com", Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LoginUser_WithAVariant_TokenCarriesTheNormalizedEmail()
    {
        await RegisterAsync("login.token.claim@example.com");

        var response = await LoginAsync(" Login.Token.Claim@EXAMPLE.com ", Password);

        var token = (await response.Content.ReadFromJsonAsync<LoginUserResponse>())!.Token;
        var email = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Single(c => c.Type == JwtRegisteredClaimNames.Email).Value;
        Assert.Equal("login.token.claim@example.com", email);
    }

    // The endpoint answers a bare 401; the User.InvalidCredentials error is pinned on the handler
    // (AuthenticationHandlerTests)
    [Fact]
    public async Task LoginUser_UnknownVariantAndWrongPasswordOnAVariant_ReturnIdenticalResponses()
    {
        await RegisterAsync(RegisteredEmail);

        var unknown = await LoginAsync(" Login_Ghost@Example.com ", Password);
        var wrongPassword = await LoginAsync(" Login_Variant@Example.com ", WrongPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, wrongPassword.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await wrongPassword.Content.ReadAsStringAsync());
    }

    // --- AC7: validation on the trimmed address ---

    [Fact]
    public async Task LoginUser_WhitespaceOnlyEmail_Returns400()
    {
        var response = await LoginAsync(" \t ", Password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LoginUser_MalformedEmailWithSurroundingSpaces_Returns400()
    {
        var response = await LoginAsync(" not-an-email ", Password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> RegisterAsync(string email) =>
        _client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password = password });

    private record LoginUserResponse(string Token);
}

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

public class LoginUserTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string RoleClaim = "role";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task LoginUser_WithValidCredentials_Returns200WithToken()
    {
        await _client.PostAsJsonAsync(RegisterEndpoint,
            new { Email = "login@example.com", Password = "Password1!" });

        var response = await _client.PostAsJsonAsync(LoginEndpoint,
            new { Email = "login@example.com", Password = "Password1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginUserResponse>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Token));
    }

    [Fact]
    public async Task LoginUser_WithWrongPassword_Returns401()
    {
        await _client.PostAsJsonAsync(RegisterEndpoint,
            new { Email = "wrongpass@example.com", Password = "Password1!" });

        var response = await _client.PostAsJsonAsync(LoginEndpoint,
            new { Email = "wrongpass@example.com", Password = "WrongPassword!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginUser_WithNonExistentEmail_Returns401()
    {
        var response = await _client.PostAsJsonAsync(LoginEndpoint,
            new { Email = "ghost@example.com", Password = "Password1!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginUser_WithEmptyEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync(LoginEndpoint,
            new { Email = "", Password = "Password1!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LoginUser_UnknownEmailAndWrongPassword_ReturnIdenticalResponses()
    {
        await _client.PostAsJsonAsync(RegisterEndpoint,
            new { Email = "same-failure@example.com", Password = "Password1!" });

        var unknown = await _client.PostAsJsonAsync(LoginEndpoint,
            new { Email = "same-failure-ghost@example.com", Password = "Password1!" });
        var wrongPassword = await _client.PostAsJsonAsync(LoginEndpoint,
            new { Email = "same-failure@example.com", Password = "WrongPassword!" });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, wrongPassword.StatusCode);
        Assert.Equal(
            await unknown.Content.ReadAsStringAsync(),
            await wrongPassword.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LoginUser_WithValidCredentials_TokenClaimsMatchTheUser()
    {
        const string email = "Claims.User@Example.com";
        var register = await _client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password = "Password1!" });
        var userId = (await register.Content.ReadFromJsonAsync<RegisterUserResponse>())!.UserId;

        var response = await _client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password = "Password1!" });
        var token = (await response.Content.ReadFromJsonAsync<LoginUserResponse>())!.Token;

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();
        Assert.Equal(userId.ToString(), claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(email, claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(UserRoles.User, claims.Single(c => c.Type == RoleClaim).Value);
    }

    private record LoginUserResponse(string Token);
    private record RegisterUserResponse(Guid UserId);
}

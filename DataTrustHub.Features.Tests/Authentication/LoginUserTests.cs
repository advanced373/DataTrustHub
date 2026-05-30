using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

public class LoginUserTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
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

    private record LoginUserResponse(string Token);
}

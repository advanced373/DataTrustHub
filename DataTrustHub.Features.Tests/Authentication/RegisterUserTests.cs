using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

public class RegisterUserTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task RegisterUser_WithValidData_Returns200WithUserId()
    {
        var response = await _client.PostAsJsonAsync(
            RegisterEndpoint,
            new { Email = "valid@example.com", Password = "Password1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RegisterUserResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.UserId);
    }

    [Fact]
    public async Task RegisterUser_WithDuplicateEmail_Returns409()
    {
        var body = new { Email = "duplicate@example.com", Password = "Password1!" };
        await _client.PostAsJsonAsync(RegisterEndpoint, body);

        var response = await _client.PostAsJsonAsync(RegisterEndpoint, body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RegisterUser_WithInvalidEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            RegisterEndpoint,
            new { Email = "not-an-email", Password = "Password1!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterUser_WithShortPassword_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            RegisterEndpoint,
            new { Email = "user@example.com", Password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private record RegisterUserResponse(Guid UserId);
}

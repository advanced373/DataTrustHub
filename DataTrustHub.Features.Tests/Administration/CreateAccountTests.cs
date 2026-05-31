using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class CreateAccountTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private const string CreateAccountEndpoint = "/admin/accounts";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateAccount_WithValidData_Returns200WithUserId()
    {
        var response = await _client.PostAsJsonAsync(
            CreateAccountEndpoint,
            new { Email = "newadmin@example.com", Password = "Password1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateAccountResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.UserId);
    }

    [Fact]
    public async Task CreateAccount_WithDuplicateEmail_Returns409()
    {
        var body = new { Email = "duplicate-admin@example.com", Password = "Password1!" };
        await _client.PostAsJsonAsync(CreateAccountEndpoint, body);

        var response = await _client.PostAsJsonAsync(CreateAccountEndpoint, body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_WithInvalidEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            CreateAccountEndpoint,
            new { Email = "not-an-email", Password = "Password1!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_WithShortPassword_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            CreateAccountEndpoint,
            new { Email = "admin2@example.com", Password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private record CreateAccountResponse(Guid UserId);
}

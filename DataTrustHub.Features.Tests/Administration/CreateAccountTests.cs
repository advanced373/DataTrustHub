using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class CreateAccountTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private const string CreateAccountEndpoint = "/admin/accounts";
    private const string ValidPassword = "Password1!";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateAccount_WithValidData_Returns200WithUserId()
    {
        var response = await _client.PostAsJsonAsync(
            CreateAccountEndpoint,
            new { Email = "newadmin@example.com", Password = ValidPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateAccountResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.UserId);
    }

    [Fact]
    public async Task CreateAccount_WithValidData_StoresRegularUserWithExactEmailAndHashedPassword()
    {
        const string mixedCaseEmail = "Mixed.Case.Account@Example.com";

        var response = await _client.PostAsJsonAsync(
            CreateAccountEndpoint,
            new { Email = mixedCaseEmail, Password = ValidPassword });

        var userId = (await response.Content.ReadFromJsonAsync<CreateAccountResponse>())!.UserId;
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<DContext>().Users.SingleAsync(u => u.Id == userId);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        Assert.Equal((mixedCaseEmail, UserRoles.User), (stored.Email, stored.Role));
        Assert.NotEqual(ValidPassword, stored.HashedPassword);
        Assert.True(hasher.VerifyHashedPassword(stored.HashedPassword, ValidPassword));
    }

    [Fact]
    public async Task CreateAccount_WithDuplicateEmail_Returns409()
    {
        var body = new { Email = "duplicate-admin@example.com", Password = ValidPassword };
        await _client.PostAsJsonAsync(CreateAccountEndpoint, body);

        var response = await _client.PostAsJsonAsync(CreateAccountEndpoint, body);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.AccountEmailAlreadyInUse, AdminErrorMessages.AccountEmailAlreadyInUse);
    }

    [Fact]
    public async Task CreateAccount_WithInvalidEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            CreateAccountEndpoint,
            new { Email = "not-an-email", Password = ValidPassword });

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

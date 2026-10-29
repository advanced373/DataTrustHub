using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

public class UserRoleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string AdminOrganizationsEndpoint = "/admin/organizations";
    private const string RoleClaim = "role";
    private const string Password = "Password1!";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task RegisterUser_NewUser_GetsUserRoleInToken()
    {
        var token = await RegisterAndLoginAsync("role-user@example.com");

        Assert.Equal(UserRoles.User, ReadRole(token));
    }

    [Fact]
    public async Task LoginUser_AdminUser_TokenGrantsAccessToAdminEndpoints()
    {
        await RegisterAsync("role-admin@example.com");
        await PromoteToAdminAsync("role-admin@example.com");
        var token = await LoginAsync("role-admin@example.com");

        var response = await PostAdminOrganizationAsync(token, "Admins organization");

        Assert.Equal(UserRoles.Admin, ReadRole(token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LoginUser_RegularUser_IsForbiddenOnAdminEndpoints()
    {
        var token = await RegisterAndLoginAsync("role-regular@example.com");

        var response = await PostAdminOrganizationAsync(token, "Regular organization");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<string> RegisterAndLoginAsync(string email)
    {
        await RegisterAsync(email);
        return await LoginAsync(email);
    }

    private Task RegisterAsync(string email) =>
        _client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });

    private async Task<string> LoginAsync(string email)
    {
        var response = await _client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password });
        var result = await response.Content.ReadFromJsonAsync<LoginUserResponse>();
        return result!.Token;
    }

    private async Task PromoteToAdminAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.Role = UserRoles.Admin;
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> PostAdminOrganizationAsync(string token, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AdminOrganizationsEndpoint)
        {
            Content = JsonContent.Create(new { Name = name })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private static string? ReadRole(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .FirstOrDefault(claim => claim.Type == RoleClaim)?.Value;

    private record LoginUserResponse(string Token);
}

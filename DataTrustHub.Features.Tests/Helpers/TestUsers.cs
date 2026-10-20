using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.Helpers;

/// <summary>
/// Registers real users through the API and returns clients authenticated with their JWT.
/// </summary>
internal static class TestUsers
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string Password = "Password1!";

    internal static async Task<HttpClient> CreateClientAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory, string email, string role = UserRoles.User)
        where TEntryPoint : class
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });

        // The role is read at login, so it must be set before the token is issued
        if (role != UserRoles.User) await SetRoleAsync(factory, email, role);

        var login = await client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    internal static async Task<Guid> GetIdAsync<TEntryPoint>(WebApplicationFactory<TEntryPoint> factory, string email)
        where TEntryPoint : class
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return (await db.Users.SingleAsync(u => u.Email == email)).Id;
    }

    private static async Task SetRoleAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory, string email, string role)
        where TEntryPoint : class
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.Role = role;
        await db.SaveChangesAsync();
    }

    private record LoginResponse(string Token);
}

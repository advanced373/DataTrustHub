using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Users;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.Helpers;

/// <summary>
/// Creates real users and returns clients authenticated with their JWT. Register only submits a registration
/// request (ADR-0026), so users are stored through the host's own repository, password hasher and unit of
/// work, as an approved account would be; the token comes from the real login endpoint.
/// </summary>
internal static class TestUsers
{
    private const string LoginEndpoint = "/auth/login";
    internal const string Password = "Password1!";

    internal static async Task<HttpClient> CreateClientAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory, string email, string role = UserRoles.User)
        where TEntryPoint : class
    {
        await CreateAsync(factory, email);

        // The role is read at login, so it must be set before the token is issued
        if (role != UserRoles.User) await SetRoleAsync(factory, email, role);

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Stores a user account (role User) with the given credentials and returns its id. An email that already
    /// has an account is left as it is (as registering it again was refused before), so its id is returned.
    /// </summary>
    internal static async Task<Guid> CreateAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory, string email, string password = Password)
        where TEntryPoint : class
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var existing = await users.GetByEmailAsync(email, default);
        if (existing is not null) return existing.Id;

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = User.Register(email, hasher.HashPassword(password));
        users.Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
        return user.Id;
    }

    internal static async Task<Guid> GetIdAsync<TEntryPoint>(WebApplicationFactory<TEntryPoint> factory, string email)
        where TEntryPoint : class
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var storedEmail = Stored(email);
        return (await db.Users.SingleAsync(u => u.Email == storedEmail)).Id;
    }

    private static async Task SetRoleAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory, string email, string role)
        where TEntryPoint : class
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var storedEmail = Stored(email);
        var user = await db.Users.SingleAsync(u => u.Email == storedEmail);
        user.Role = role;
        await db.SaveChangesAsync();
    }

    // Emails are stored in their normalized form (U7), whatever case or spaces the test registered with
    private static string Stored(string email) => EmailAddress.From(email).Value;

    private record LoginResponse(string Token);
}

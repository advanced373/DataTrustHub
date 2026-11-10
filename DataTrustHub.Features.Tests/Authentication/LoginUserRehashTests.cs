using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

// AC11 of #167, end to end through the API: a user stored with the legacy unsalted SHA-256 hash signs in, and
// afterwards the stored row holds an Identity V3 hash (re-hash on sign-in)
public class LoginUserRehashTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string LoginEndpoint = "/auth/login";
    private const string Password = "Password1!";
    private const byte IdentityV3FormatMarker = 0x01;
    private const string LoginUserAction = "LoginUser";
    private const string SucceededOutcome = "Succeeded";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task LoginUser_LegacyHashedUser_Returns200WithToken()
    {
        const string email = "legacy.token@example.com";
        await SeedLegacyUserAsync(email);

        var response = await _client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrEmpty(body!.Token));
    }

    [Fact]
    public async Task LoginUser_LegacyHashedUser_StoresAV3HashThatVerifiesTheSamePassword()
    {
        const string email = "legacy.rehash@example.com";
        await SeedLegacyUserAsync(email);

        await _client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password });

        var storedHash = await StoredHashAsync(email);
        Assert.Equal(IdentityV3FormatMarker, Convert.FromBase64String(storedHash)[0]);
        using var scope = factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(storedHash, Password));
    }

    [Fact]
    public async Task LoginUser_LegacyHashedUserSignsInTwice_SecondSignInSucceedsWithTheNewHash()
    {
        const string email = "legacy.twice@example.com";
        await SeedLegacyUserAsync(email);

        await _client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password });
        var rehashed = await StoredHashAsync(email);
        var second = await _client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(rehashed, await StoredHashAsync(email));
    }

    [Fact]
    public async Task LoginUser_LegacyHashedUserWrongPassword_Returns401AndKeepsTheLegacyHash()
    {
        const string email = "legacy.wrong@example.com";
        var legacyHash = await SeedLegacyUserAsync(email);

        var response = await _client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password = "WrongPassword1!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(legacyHash, await StoredHashAsync(email));
    }

    // AC3 of #134: the sign-in that replaces the hash is recorded with the signed-in user as its actor
    [Fact]
    public async Task LoginUser_LegacyHashedUser_RecordsTheSignedInUserAsActor()
    {
        const string email = "legacy.actor@example.com";
        await SeedLegacyUserAsync(email);
        var userId = await TestUsers.GetIdAsync(factory, email);

        await _client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password });

        // The hash was replaced, so this sign-in took the re-hash path (U8)
        Assert.Equal(IdentityV3FormatMarker, Convert.FromBase64String(await StoredHashAsync(email))[0]);
        var record = Assert.Single(await LoginRecordsOfAsync(userId));
        Assert.Equal((SucceededOutcome, (string?)null), (record.Outcome, record.ErrorCode));
    }

    // Stores the user as it was stored before #167: the unsalted SHA-256 hash of the password
    private async Task<string> SeedLegacyUserAsync(string email)
    {
        var legacyHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Password)));
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IUserRepository>().Add(User.Register(email, legacyHash));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return legacyHash;
    }

    private async Task<string> StoredHashAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return (await db.Users.AsNoTracking().SingleAsync(u => u.Email == email)).HashedPassword;
    }

    private async Task<List<DbAuditRecord>> LoginRecordsOfAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return await db.AuditRecords.AsNoTracking()
            .Where(r => r.Action == LoginUserAction && r.ActorUserId == userId)
            .ToListAsync();
    }

    private record LoginResponse(string Token);
}

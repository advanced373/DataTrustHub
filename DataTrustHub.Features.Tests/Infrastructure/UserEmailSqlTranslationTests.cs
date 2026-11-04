using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Infrastructure;

// AC8 of #123 on a relational provider: IUserRepository.GetByEmailAsync and EmailExistsAsync normalize the
// argument and compare by equality in the database. SQLite compares text case-sensitively and refuses a query
// it cannot translate, so a variant is found only through the normalized argument, never by client evaluation.
// AC1: rebuilding a stored user does not change what is stored
public class UserEmailSqlTranslationTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    private const string Hash = "hash";
    private const string EmailDomain = "@example.com";

    // --- the repository ---

    [Fact]
    public async Task GetByEmailAsync_CaseOrSpaceVariant_FindsTheStoredUser()
    {
        var email = UniqueEmail("sql.repo.get");
        var user = await StoreUserAsync(email);

        var found = await InScopeAsync<User?>(repo => repo.GetByEmailAsync(Variant(email), default));

        Assert.Equal((user.Id, email), (found!.Id, found.Email));
    }

    [Fact]
    public async Task EmailExistsAsync_CaseOrSpaceVariant_ReturnsTrue()
    {
        var email = UniqueEmail("sql.repo.exists");
        await StoreUserAsync(email);

        Assert.True(await InScopeAsync(repo => repo.EmailExistsAsync(Variant(email), default)));
    }

    [Fact]
    public async Task EmailExistsAsync_VariantOfAnotherAddress_ReturnsFalse()
    {
        await StoreUserAsync("sql.repo.other@example.com");

        Assert.False(await InScopeAsync(repo => repo.EmailExistsAsync(" SQL.Repo.Other@Example.co ", default)));
    }

    [Fact]
    public async Task GetByEmailAsync_VariantOfAnotherAddress_ReturnsNull()
    {
        await StoreUserAsync("sql.repo.none@example.com");

        Assert.Null(await InScopeAsync<User?>(repo => repo.GetByEmailAsync(" SQL.Repo.Non@Example.com ", default)));
    }

    // A row stored before the data migration keeps its email when it is rebuilt into a User
    [Fact]
    public async Task GetByIdAsync_RowWithANonNormalizedEmail_RebuildsTheUserWithTheStoredEmail()
    {
        const string legacyEmail = " Legacy.Stored@Example.COM ";
        var id = Guid.NewGuid();
        await InsertRowAsync(new DbUser { Id = id, Email = legacyEmail, HashedPassword = Hash, Role = UserRoles.User });

        var rebuilt = await InScopeAsync<User?>(repo => repo.GetByIdAsync(id, default));

        Assert.Equal(legacyEmail, rebuilt!.Email);
    }

    // --- through the API ---

    // Register and log in go through the repository above; find user by email reads DContext itself
    [Fact]
    public async Task FindUserByEmail_OnSqlite_CaseOrSpaceVariant_ReturnsTheNormalizedEmail()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "sql_find_norm");
        var (_, userId) = await CreateUserAsync(factory, "sql_find_norm_target");

        var response = await PostFindUserByEmailAsync(
            scenario.OrgAdmin, scenario.OrganizationId, " SQL_Find_Norm_Target@Example.COM ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var found = (await response.Content.ReadFromJsonAsync<FoundUser>())!;
        Assert.Equal((userId, "sql_find_norm_target@example.com"), (found.UserId, found.Email));
    }

    // --- helpers ---

    private static string UniqueEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}{EmailDomain}";

    private static string Variant(string email) => "\t " + email.ToUpperInvariant() + " \n";

    private async Task<User> StoreUserAsync(string email)
    {
        var user = User.Register(email, Hash);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
        return user;
    }

    private async Task InsertRowAsync(DbUser row)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        db.Users.Add(row);
        await db.SaveChangesAsync();
    }

    private async Task<TResult> InScopeAsync<TResult>(Func<IUserRepository, Task<TResult>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IUserRepository>());
    }

    private record FoundUser(Guid UserId, string Email, bool IsMember, string? Role);
}

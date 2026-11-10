using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Memberships;
using DataTrustHub.Domain.ReadModels.Registrations;
using DataTrustHub.Domain.ReadModels.Users;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #74 part 4d, AC5 on SQL Server itself (a throwaway LocalDB database built by the migrations; skipped where LocalDB
// is missing): each new method of the account, user lookup and registration request read side translates and runs
// in the database as a projection, with the order and the exact normalized email match AC3 names. The *_TracksNothing
// tests are regression guards (see AssertTracksNothingAsync). One scenario per test, with fresh rows, so the tests
// share the database without seeing each other's rows
public sealed class AccountReadSideSqlServerTests(LocalDbApiFactory factory) : IClassFixture<LocalDbApiFactory>
{
    private const string EmailDomain = "@readside-accounts.test";
    private const string HashedPassword = "not-a-real-hash";
    private const string WildcardPattern = "%";

    // Email prefixes, chosen so the order by email differs from the order of insertion
    private const string FirstEmailPrefix = "a";
    private const string SecondEmailPrefix = "m";
    private const string ThirdEmailPrefix = "z";

    private const int LastGuidByte = 15;

    private static readonly DateTime BaseRequestedAt = new(2026, 11, 1, 8, 30, 0, DateTimeKind.Utc);

    // --- IUserQueries.ListAccountsAsync ---

    [LocalDbFact]
    public async Task ListAccounts_OnSqlServer_ReturnsEachAccountWithIdEmailAndRoleOrderedByEmail()
    {
        var scenario = await SeedUsersAsync();

        var accounts = await WithQueriesAsync<IUserQueries, IReadOnlyList<AccountEntry>>(
            queries => queries.ListAccountsAsync(CancellationToken.None));

        Assert.Equal(
            [scenario.First, scenario.Second, scenario.Third],
            accounts.Where(account => scenario.Ids.Contains(account.Id)));
    }

    [LocalDbFact]
    public async Task ListAccounts_OnSqlServer_TracksNothing()
    {
        await SeedUsersAsync();

        await AssertTracksNothingAsync<IUserQueries>(queries => queries.ListAccountsAsync(CancellationToken.None));
    }

    // --- IUserQueries.FindAccountAsync ---

    [LocalDbFact]
    public async Task FindAccount_OnSqlServer_ExistingUser_ReturnsItsIdEmailAndRole()
    {
        var scenario = await SeedUsersAsync();

        var account = await WithQueriesAsync<IUserQueries, AccountEntry?>(
            queries => queries.FindAccountAsync(scenario.First.Id, CancellationToken.None));

        Assert.Equal(scenario.First, account);
    }

    [LocalDbFact]
    public async Task FindAccount_OnSqlServer_MissingUser_ReturnsNull()
    {
        await SeedUsersAsync();

        Assert.Null(await WithQueriesAsync<IUserQueries, AccountEntry?>(
            queries => queries.FindAccountAsync(Guid.NewGuid(), CancellationToken.None)));
    }

    [LocalDbFact]
    public async Task FindAccount_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedUsersAsync();

        await AssertTracksNothingAsync<IUserQueries>(
            queries => queries.FindAccountAsync(scenario.Second.Id, CancellationToken.None));
    }

    // --- IUserQueries.FindByEmailAsync ---

    [LocalDbFact]
    public async Task FindByEmail_OnSqlServer_NormalizedAddress_ReturnsTheUsersIdAndEmail()
    {
        var scenario = await SeedUsersAsync();

        var user = await FindByEmailAsync(scenario.Second.Email);

        Assert.Equal(new UserByEmail(scenario.Second.Id, scenario.Second.Email), user);
    }

    // Equality, not a pattern match: a prefix of a stored address, or a LIKE wildcard, finds nobody
    [LocalDbFact]
    public async Task FindByEmail_OnSqlServer_PrefixOfAStoredAddress_ReturnsNull()
    {
        var scenario = await SeedUsersAsync();

        Assert.Null(await FindByEmailAsync(scenario.Second.Email[..^1]));
    }

    [LocalDbFact]
    public async Task FindByEmail_OnSqlServer_WildcardAddress_ReturnsNull()
    {
        await SeedUsersAsync();

        Assert.Null(await FindByEmailAsync(WildcardPattern + EmailDomain));
        Assert.Null(await FindByEmailAsync(WildcardPattern));
    }

    [LocalDbFact]
    public async Task FindByEmail_OnSqlServer_UnknownAddress_ReturnsNull()
    {
        await SeedUsersAsync();

        Assert.Null(await FindByEmailAsync(Guid.NewGuid().ToString("N") + EmailDomain));
    }

    [LocalDbFact]
    public async Task FindByEmail_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedUsersAsync();

        await AssertTracksNothingAsync<IUserQueries>(
            queries => queries.FindByEmailAsync(scenario.First.Email, CancellationToken.None));
    }

    // --- IMembershipQueries.FindRoleAsync ---

    [LocalDbFact]
    public async Task FindRole_OnSqlServer_UserWithMembershipThere_ReturnsTheRole()
    {
        var scenario = await SeedMembershipsAsync();

        var role = await FindRoleAsync(scenario.UserId, scenario.OrganizationId);

        Assert.Equal(MembershipRole.Guest.Name, role);
    }

    // The user has a membership (control: its own organization finds it), only not in this organization
    [LocalDbFact]
    public async Task FindRole_OnSqlServer_MembershipOnlyInAnotherOrganization_ReturnsNull()
    {
        var scenario = await SeedMembershipsAsync();
        Assert.Equal(MembershipRole.OrgAdmin.Name, await FindRoleAsync(scenario.UserId, scenario.OtherOrganizationId));

        Assert.Null(await FindRoleAsync(scenario.UserId, Guid.NewGuid()));
    }

    // Another user's membership in the organization is not the user's
    [LocalDbFact]
    public async Task FindRole_OnSqlServer_UserWithoutMembership_ReturnsNull()
    {
        var scenario = await SeedMembershipsAsync();

        Assert.Null(await FindRoleAsync(scenario.StrangerId, scenario.OrganizationId));
    }

    [LocalDbFact]
    public async Task FindRole_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedMembershipsAsync();

        await AssertTracksNothingAsync<IMembershipQueries>(
            queries => queries.FindRoleAsync(scenario.UserId, scenario.OrganizationId, CancellationToken.None));
    }

    // --- IRegistrationRequestQueries.ListPendingAsync ---

    [LocalDbFact]
    public async Task ListPending_OnSqlServer_ReturnsIdEmailAndRequestedAtOldestFirstThenById()
    {
        var expected = await SeedRegistrationRequestsAsync();

        var pending = await WithQueriesAsync<IRegistrationRequestQueries, IReadOnlyList<PendingRegistrationRequest>>(
            queries => queries.ListPendingAsync(CancellationToken.None));

        var ids = expected.Select(request => request.Id).ToHashSet();
        Assert.Equal(expected, pending.Where(request => ids.Contains(request.Id)));
    }

    [LocalDbFact]
    public async Task ListPending_OnSqlServer_TracksNothing()
    {
        await SeedRegistrationRequestsAsync();

        await AssertTracksNothingAsync<IRegistrationRequestQueries>(
            queries => queries.ListPendingAsync(CancellationToken.None));
    }

    // --- helpers ---

    private Task<UserByEmail?> FindByEmailAsync(string normalizedEmail) =>
        WithQueriesAsync<IUserQueries, UserByEmail?>(
            queries => queries.FindByEmailAsync(normalizedEmail, CancellationToken.None));

    private Task<string?> FindRoleAsync(Guid userId, Guid organizationId) =>
        WithQueriesAsync<IMembershipQueries, string?>(
            queries => queries.FindRoleAsync(userId, organizationId, CancellationToken.None));

    // The port as a request gets it: resolved from a new scope of the API container
    private async Task<TResult> WithQueriesAsync<TPort, TResult>(Func<TPort, Task<TResult>> read)
        where TPort : notnull
    {
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<TPort>());
    }

    // A regression guard only: the request's context holds no entity after the read. Every method projects to a
    // record or a value, and EF never tracks a non-entity projection, so this would still pass without AsNoTracking.
    // It fails if a method starts returning or materializing tracked entities
    private async Task AssertTracksNothingAsync<TPort>(Func<TPort, Task> read)
        where TPort : notnull
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();

        await read(scope.ServiceProvider.GetRequiredService<TPort>());

        Assert.Empty(db.ChangeTracker.Entries());
    }

    // Three users inserted z, a, m, so the order by email differs from the order of insertion; one is a platform admin
    private async Task<UserScenario> SeedUsersAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();

        var third = NewUser(ThirdEmailPrefix, UserRoles.User);
        var first = NewUser(FirstEmailPrefix, UserRoles.Admin);
        var second = NewUser(SecondEmailPrefix, UserRoles.User);
        db.Users.AddRange(third, first, second);
        await db.SaveChangesAsync();

        return new UserScenario(Expected(first), Expected(second), Expected(third));
    }

    // The user is a guest of one organization and the org admin of another; a stranger is a member of neither, while
    // a third user is a member of the first organization
    private async Task<MembershipScenario> SeedMembershipsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();

        var user = NewUser(FirstEmailPrefix, UserRoles.User);
        var stranger = NewUser(SecondEmailPrefix, UserRoles.User);
        var colleague = NewUser(ThirdEmailPrefix, UserRoles.User);
        db.Users.AddRange(user, stranger, colleague);
        db.Memberships.AddRange(
            NewMembership(colleague.Id, organizationId, MembershipRole.Member),
            NewMembership(user.Id, organizationId, MembershipRole.Guest),
            NewMembership(user.Id, otherOrganizationId, MembershipRole.OrgAdmin));
        await db.SaveChangesAsync();

        return new MembershipScenario(user.Id, stranger.Id, organizationId, otherOrganizationId);
    }

    // Four requests inserted out of order: the newest, then two made at the same moment (whose ids differ only in the
    // last byte, which SQL Server and .NET order alike), then the oldest. Returns them in the order AC3 names
    private async Task<IReadOnlyList<PendingRegistrationRequest>> SeedRegistrationRequestsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var tieBytes = Guid.NewGuid().ToByteArray();

        var oldest = NewRequest(Guid.NewGuid(), BaseRequestedAt);
        var tiedLow = NewRequest(WithLastByte(tieBytes, 0x01), BaseRequestedAt.AddHours(1));
        var tiedHigh = NewRequest(WithLastByte(tieBytes, 0x02), BaseRequestedAt.AddHours(1));
        var newest = NewRequest(Guid.NewGuid(), BaseRequestedAt.AddHours(2));
        db.RegistrationRequests.AddRange(newest, tiedHigh, tiedLow, oldest);
        await db.SaveChangesAsync();

        return [Expected(oldest), Expected(tiedLow), Expected(tiedHigh), Expected(newest)];
    }

    private static Guid WithLastByte(byte[] bytes, byte last)
    {
        var copy = (byte[])bytes.Clone();
        copy[LastGuidByte] = last;
        return new Guid(copy);
    }

    // What the read side must answer for a stored row, built from the values seeded, not read back
    private static AccountEntry Expected(DbUser user) => new(user.Id, user.Email, user.Role);

    private static PendingRegistrationRequest Expected(DbRegistrationRequest request) =>
        new(request.Id, request.Email, request.RequestedAt);

    private static DbUser NewUser(string emailPrefix, string role) =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = emailPrefix + Guid.NewGuid().ToString("N") + EmailDomain,
            HashedPassword = HashedPassword,
            Role = role
        };

    private static DbMembership NewMembership(Guid userId, Guid organizationId, MembershipRole role) =>
        new() { Id = Guid.NewGuid(), UserId = userId, OrganizationId = organizationId, Role = role.Name };

    private static DbRegistrationRequest NewRequest(Guid id, DateTime requestedAt) =>
        new()
        {
            Id = id,
            Email = Guid.NewGuid().ToString("N") + EmailDomain,
            HashedPassword = HashedPassword,
            RequestedAt = requestedAt
        };

    private sealed record UserScenario(AccountEntry First, AccountEntry Second, AccountEntry Third)
    {
        internal HashSet<Guid> Ids => [First.Id, Second.Id, Third.Id];
    }

    private sealed record MembershipScenario(Guid UserId, Guid StrangerId, Guid OrganizationId, Guid OtherOrganizationId);
}

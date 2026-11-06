using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Memberships;
using DataTrustHub.Domain.ReadModels.Organizations;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #74 part 4b, AC5 on SQL Server itself (a throwaway LocalDB database built by the migrations; skipped where LocalDB
// is missing): each method of the organization and membership read side translates and runs in the database as a
// projection, in the order AC3 names. The *_TracksNothing tests are regression guards (see AssertTracksNothingAsync).
// One scenario per test, with fresh users and organizations, so the tests share the database without seeing each
// other's rows
public sealed class AdministrationReadSideSqlServerTests(LocalDbApiFactory factory) : IClassFixture<LocalDbApiFactory>
{
    private const string EmailDomain = "@readside-admin.test";
    private const string HashedPassword = "not-a-real-hash";

    // Organization names and email prefixes, chosen so the order by name (email) differs from the order of insertion
    private const string FirstOrganizationPrefix = "a ";
    private const string SecondOrganizationPrefix = "m ";
    private const string ThirdOrganizationPrefix = "z ";
    private const string GoneOrganizationPrefix = "b gone ";
    private const string FirstEmailPrefix = "a";
    private const string SecondEmailPrefix = "m";
    private const string ThirdEmailPrefix = "z";

    // --- IOrganizationQueries.ListAsync ---

    [LocalDbFact]
    public async Task ListOrganizations_OnSqlServer_ReturnsEachOrganizationWithIdAndNameOrderedByName()
    {
        var scenario = await SeedAsync();

        var entries = await WithQueriesAsync<IOrganizationQueries, IReadOnlyList<OrganizationEntry>>(
            queries => queries.ListAsync(CancellationToken.None));

        Assert.Equal(
            [
                new OrganizationEntry(scenario.First.Id, scenario.First.Name),
                new OrganizationEntry(scenario.Second.Id, scenario.Second.Name),
                new OrganizationEntry(scenario.Third.Id, scenario.Third.Name)
            ],
            entries.Where(entry => scenario.OrganizationIds.Contains(entry.Id)));
    }

    [LocalDbFact]
    public async Task ListOrganizations_OnSqlServer_DeletedOrganization_IsNotListed()
    {
        var scenario = await SeedAsync();

        var entries = await WithQueriesAsync<IOrganizationQueries, IReadOnlyList<OrganizationEntry>>(
            queries => queries.ListAsync(CancellationToken.None));

        Assert.DoesNotContain(entries, entry => entry.Id == scenario.GoneOrganizationId);
    }

    [LocalDbFact]
    public async Task ListOrganizations_OnSqlServer_TracksNothing()
    {
        await SeedAsync();

        await AssertTracksNothingAsync<IOrganizationQueries>(queries => queries.ListAsync(CancellationToken.None));
    }

    // --- IOrganizationQueries.FindAsync ---

    [LocalDbFact]
    public async Task FindOrganization_OnSqlServer_ExistingOrganization_ReturnsItsIdAndName()
    {
        var scenario = await SeedAsync();

        var entry = await FindOrganizationAsync(scenario.Second.Id);

        Assert.Equal(new OrganizationEntry(scenario.Second.Id, scenario.Second.Name), entry);
    }

    [LocalDbFact]
    public async Task FindOrganization_OnSqlServer_MissingOrganization_ReturnsNull()
    {
        await SeedAsync();

        Assert.Null(await FindOrganizationAsync(Guid.NewGuid()));
    }

    [LocalDbFact]
    public async Task FindOrganization_OnSqlServer_DeletedOrganization_ReturnsNull()
    {
        var scenario = await SeedAsync();

        Assert.Null(await FindOrganizationAsync(scenario.GoneOrganizationId));
    }

    [LocalDbFact]
    public async Task FindOrganization_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedAsync();

        await AssertTracksNothingAsync<IOrganizationQueries>(
            queries => queries.FindAsync(scenario.First.Id, CancellationToken.None));
    }

    // --- IMembershipQueries.ListMembersAsync ---

    [LocalDbFact]
    public async Task ListMembers_OnSqlServer_ReturnsEveryMembershipWithEmailAndRoleOrderedByEmail()
    {
        var scenario = await SeedAsync();

        var members = await ListMembersAsync(scenario.First.Id);

        Assert.Equal(
            [
                new OrganizationMember(scenario.FirstMembers[0].MembershipId, scenario.FirstMembers[0].UserId,
                    scenario.FirstMembers[0].Email, MembershipRole.Guest.Name),
                new OrganizationMember(scenario.FirstMembers[1].MembershipId, scenario.FirstMembers[1].UserId,
                    scenario.FirstMembers[1].Email, MembershipRole.OrgAdmin.Name),
                new OrganizationMember(scenario.FirstMembers[2].MembershipId, scenario.FirstMembers[2].UserId,
                    scenario.FirstMembers[2].Email, MembershipRole.Member.Name)
            ],
            members);
    }

    [LocalDbFact]
    public async Task ListMembers_OnSqlServer_ReturnsOnlyThatOrganizationsMemberships()
    {
        var scenario = await SeedAsync();

        var members = await ListMembersAsync(scenario.Second.Id);

        Assert.Equal([scenario.ReaderSecondMembershipId], members.Select(member => member.MembershipId));
    }

    [LocalDbFact]
    public async Task ListMembers_OnSqlServer_OrganizationWithoutMemberships_ReturnsEmpty()
    {
        var scenario = await SeedAsync();

        Assert.Empty(await ListMembersAsync(scenario.Third.Id));
    }

    [LocalDbFact]
    public async Task ListMembers_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedAsync();

        await AssertTracksNothingAsync<IMembershipQueries>(
            queries => queries.ListMembersAsync(scenario.First.Id, CancellationToken.None));
    }

    // --- IMembershipQueries.ListMyOrganizationsAsync ---

    [LocalDbFact]
    public async Task ListMyOrganizations_OnSqlServer_ReturnsOwnMembershipsWithNameAndRoleOrderedByOrganizationName()
    {
        var scenario = await SeedAsync();

        var organizations = await ListMyOrganizationsAsync(scenario.ReaderId);

        Assert.Equal(
            [
                new MyOrganization(scenario.First.Id, scenario.First.Name, scenario.ReaderFirstMembershipId, MembershipRole.OrgAdmin.Name),
                new MyOrganization(scenario.Second.Id, scenario.Second.Name, scenario.ReaderSecondMembershipId, MembershipRole.Member.Name)
            ],
            organizations);
    }

    // The reader's membership in the deleted organization is still stored (control below): only the query leaves it out
    [LocalDbFact]
    public async Task ListMyOrganizations_OnSqlServer_MembershipOfDeletedOrganization_IsLeftOut()
    {
        var scenario = await SeedAsync();
        Assert.True(await MembershipIsStoredAsync(scenario.ReaderGoneMembershipId));

        var organizations = await ListMyOrganizationsAsync(scenario.ReaderId);

        Assert.DoesNotContain(organizations, organization => organization.MembershipId == scenario.ReaderGoneMembershipId);
        Assert.DoesNotContain(organizations, organization => organization.OrganizationId == scenario.GoneOrganizationId);
    }

    [LocalDbFact]
    public async Task ListMyOrganizations_OnSqlServer_UserWithoutMemberships_ReturnsEmpty()
    {
        await SeedAsync();
        var stranger = await SeedUserAsync(FirstEmailPrefix);

        Assert.Empty(await ListMyOrganizationsAsync(stranger.Id));
    }

    [LocalDbFact]
    public async Task ListMyOrganizations_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedAsync();

        await AssertTracksNothingAsync<IMembershipQueries>(
            queries => queries.ListMyOrganizationsAsync(scenario.ReaderId, CancellationToken.None));
    }

    // --- helpers ---

    private Task<OrganizationEntry?> FindOrganizationAsync(Guid organizationId) =>
        WithQueriesAsync<IOrganizationQueries, OrganizationEntry?>(
            queries => queries.FindAsync(organizationId, CancellationToken.None));

    private Task<IReadOnlyList<OrganizationMember>> ListMembersAsync(Guid organizationId) =>
        WithQueriesAsync<IMembershipQueries, IReadOnlyList<OrganizationMember>>(
            queries => queries.ListMembersAsync(organizationId, CancellationToken.None));

    private Task<IReadOnlyList<MyOrganization>> ListMyOrganizationsAsync(Guid userId) =>
        WithQueriesAsync<IMembershipQueries, IReadOnlyList<MyOrganization>>(
            queries => queries.ListMyOrganizationsAsync(userId, CancellationToken.None));

    // The port as a request gets it: resolved from a new scope of the API container
    private async Task<TResult> WithQueriesAsync<TPort, TResult>(Func<TPort, Task<TResult>> read)
        where TPort : notnull
    {
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<TPort>());
    }

    // A regression guard only: the request's context holds no entity after the read. Every method projects to a
    // record, and EF never tracks a non-entity projection, so this would still pass without AsNoTracking. It fails if
    // a method starts returning or materializing tracked entities. The AC5 evidence is the tests above that run each
    // method on SQL Server
    private async Task AssertTracksNothingAsync<TPort>(Func<TPort, Task> read)
        where TPort : notnull
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();

        await read(scope.ServiceProvider.GetRequiredService<TPort>());

        Assert.Empty(db.ChangeTracker.Entries());
    }

    private async Task<bool> MembershipIsStoredAsync(Guid membershipId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return await db.Memberships.FindAsync(membershipId) is not null;
    }

    // Organizations are inserted in the order Third, First, Second. First has three members, inserted so the order by
    // email differs (z member, a guest, the reader with m as org admin); the reader is also a member of Second and of
    // an organization whose row is deleted after its membership was stored. Third has no memberships
    private async Task<Scenario> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var suffix = Guid.NewGuid().ToString("N");

        var third = NewOrganization(ThirdOrganizationPrefix + suffix);
        var first = NewOrganization(FirstOrganizationPrefix + suffix);
        var second = NewOrganization(SecondOrganizationPrefix + suffix);
        var gone = NewOrganization(GoneOrganizationPrefix + suffix);
        var zed = NewUser(ThirdEmailPrefix);
        var alpha = NewUser(FirstEmailPrefix);
        var reader = NewUser(SecondEmailPrefix);
        db.AddRange(third, first, second, gone, zed, alpha, reader);

        var zedFirst = NewMembership(zed.Id, first.Id, MembershipRole.Member);
        var alphaFirst = NewMembership(alpha.Id, first.Id, MembershipRole.Guest);
        var readerFirst = NewMembership(reader.Id, first.Id, MembershipRole.OrgAdmin);
        var readerGone = NewMembership(reader.Id, gone.Id, MembershipRole.Member);
        var readerSecond = NewMembership(reader.Id, second.Id, MembershipRole.Member);
        db.Memberships.AddRange(zedFirst, alphaFirst, readerFirst, readerGone, readerSecond);
        await db.SaveChangesAsync();

        // No foreign key ties a membership to its organization: the membership stays when the organization goes
        db.Organizations.Remove(gone);
        await db.SaveChangesAsync();

        return new Scenario(
            reader.Id,
            new OrganizationRow(first.Id, first.Name),
            new OrganizationRow(second.Id, second.Name),
            new OrganizationRow(third.Id, third.Name),
            gone.Id,
            [
                new MemberRow(alphaFirst.Id, alpha.Id, alpha.Email),
                new MemberRow(readerFirst.Id, reader.Id, reader.Email),
                new MemberRow(zedFirst.Id, zed.Id, zed.Email)
            ],
            readerFirst.Id,
            readerSecond.Id,
            readerGone.Id);
    }

    private async Task<DbUser> SeedUserAsync(string emailPrefix)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var user = NewUser(emailPrefix);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static DbOrganization NewOrganization(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private static DbUser NewUser(string emailPrefix) =>
        new() { Id = Guid.NewGuid(), Email = emailPrefix + Guid.NewGuid().ToString("N") + EmailDomain, HashedPassword = HashedPassword };

    private static DbMembership NewMembership(Guid userId, Guid organizationId, MembershipRole role) =>
        new() { Id = Guid.NewGuid(), UserId = userId, OrganizationId = organizationId, Role = role.Name };

    private sealed record OrganizationRow(Guid Id, string Name);

    private sealed record MemberRow(Guid MembershipId, Guid UserId, string Email);

    private sealed record Scenario(
        Guid ReaderId,
        OrganizationRow First,
        OrganizationRow Second,
        OrganizationRow Third,
        Guid GoneOrganizationId,
        IReadOnlyList<MemberRow> FirstMembers,
        Guid ReaderFirstMembershipId,
        Guid ReaderSecondMembershipId,
        Guid ReaderGoneMembershipId)
    {
        internal HashSet<Guid> OrganizationIds => [First.Id, Second.Id, Third.Id];
    }
}

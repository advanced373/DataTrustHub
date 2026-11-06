using DataTrustHub.Domain.ReadModels.Clearances;
using DataTrustHub.Domain.ReadModels.Policies;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #74 part 4c, AC5 on SQL Server itself (a throwaway LocalDB database built by the migrations; skipped where LocalDB
// is missing): each method of the policy and clearance read side translates and runs in the database as a projection,
// with the organization scoping, the filters and the order AC3 names. The *_TracksNothing tests are regression guards
// (see AssertTracksNothingAsync). One scenario per test, with fresh ids, so the tests share the database without
// seeing each other's rows
public sealed class PolicyAndClearanceReadSideSqlServerTests(LocalDbApiFactory factory) : IClassFixture<LocalDbApiFactory>
{
    private const string EmailDomain = "@readside-policies.test";
    private const string HashedPassword = "not-a-real-hash";

    // Name prefixes, chosen so the order by name differs from the order of insertion
    private const string FirstPrefix = "a ";
    private const string SecondPrefix = "m ";
    private const string ThirdPrefix = "z ";
    private const string FourthPrefix = "b ";
    private const string ElsewherePrefix = "c ";

    private const string LowLevelName = "Internal";
    private const int LowLevelPriority = 1;
    private const string HighLevelName = "Secret";
    private const int HighLevelPriority = 2;

    // --- IPolicyQueries.ListAsync ---

    [LocalDbFact]
    public async Task ListPolicies_OnSqlServer_ReturnsOnlyThatOrganizationsPoliciesOrderedByName()
    {
        var scenario = await SeedAsync();

        var policies = await ListPoliciesAsync(scenario.OrganizationId);

        Assert.Equal(
            [
                new PolicyEntry(scenario.First.Id, scenario.First.Name, scenario.OrganizationId),
                new PolicyEntry(scenario.Second.Id, scenario.Second.Name, scenario.OrganizationId),
                new PolicyEntry(scenario.Third.Id, scenario.Third.Name, scenario.OrganizationId)
            ],
            policies);
    }

    [LocalDbFact]
    public async Task ListPolicies_OnSqlServer_OrganizationWithoutPolicies_ReturnsEmpty()
    {
        await SeedAsync();

        Assert.Empty(await ListPoliciesAsync(Guid.NewGuid()));
    }

    [LocalDbFact]
    public async Task ListPolicies_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedAsync();

        await AssertTracksNothingAsync<IPolicyQueries>(
            queries => queries.ListAsync(scenario.OrganizationId, CancellationToken.None));
    }

    // --- IPolicyQueries.FindAsync ---

    [LocalDbFact]
    public async Task FindPolicy_OnSqlServer_PolicyOfTheOrganization_ReturnsItsIdNameAndOrganization()
    {
        var scenario = await SeedAsync();

        var policy = await FindPolicyAsync(scenario.OrganizationId, scenario.Second.Id);

        Assert.Equal(new PolicyEntry(scenario.Second.Id, scenario.Second.Name, scenario.OrganizationId), policy);
    }

    // The policy exists (control: its own organization finds it), only not in this organization
    [LocalDbFact]
    public async Task FindPolicy_OnSqlServer_PolicyOfAnotherOrganization_ReturnsNull()
    {
        var scenario = await SeedAsync();
        Assert.NotNull(await FindPolicyAsync(scenario.OtherOrganizationId, scenario.Elsewhere.Id));

        Assert.Null(await FindPolicyAsync(scenario.OrganizationId, scenario.Elsewhere.Id));
    }

    [LocalDbFact]
    public async Task FindPolicy_OnSqlServer_MissingPolicy_ReturnsNull()
    {
        var scenario = await SeedAsync();

        Assert.Null(await FindPolicyAsync(scenario.OrganizationId, Guid.NewGuid()));
    }

    [LocalDbFact]
    public async Task FindPolicy_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedAsync();

        await AssertTracksNothingAsync<IPolicyQueries>(
            queries => queries.FindAsync(scenario.OrganizationId, scenario.First.Id, CancellationToken.None));
    }

    // --- IClearanceQueries.ListAsync ---

    [LocalDbFact]
    public async Task ListClearances_OnSqlServer_NoFilters_ReturnsOnlyClearancesOnTheOrganizationsPoliciesOrderedByName()
    {
        var scenario = await SeedAsync();

        var clearances = await ListClearancesAsync(scenario.OrganizationId, null, null);

        Assert.Equal(
            [scenario.ReaderOnFirst, scenario.OtherOnSecond, scenario.ReaderOnSecond, scenario.OtherOnFirst],
            clearances);
    }

    [LocalDbFact]
    public async Task ListClearances_OnSqlServer_UserFilter_ReturnsOnlyThatUsersClearancesHereOrderedByName()
    {
        var scenario = await SeedAsync();

        var clearances = await ListClearancesAsync(scenario.OrganizationId, scenario.ReaderId, null);

        Assert.Equal([scenario.ReaderOnFirst, scenario.ReaderOnSecond], clearances);
    }

    [LocalDbFact]
    public async Task ListClearances_OnSqlServer_PolicyFilter_ReturnsOnlyThatPolicysClearancesOrderedByName()
    {
        var scenario = await SeedAsync();

        var clearances = await ListClearancesAsync(scenario.OrganizationId, null, scenario.First.Id);

        Assert.Equal([scenario.ReaderOnFirst, scenario.OtherOnFirst], clearances);
    }

    [LocalDbFact]
    public async Task ListClearances_OnSqlServer_UserAndPolicyFilters_ReturnOnlyThatUsersClearanceOnThatPolicy()
    {
        var scenario = await SeedAsync();

        var clearances = await ListClearancesAsync(scenario.OrganizationId, scenario.ReaderId, scenario.Second.Id);

        Assert.Equal([scenario.ReaderOnSecond], clearances);
    }

    // The clearance exists (control: its own organization lists it), only not on a policy of this organization
    [LocalDbFact]
    public async Task ListClearances_OnSqlServer_PolicyOfAnotherOrganization_ReturnsEmpty()
    {
        var scenario = await SeedAsync();
        Assert.Equal(
            [scenario.ReaderElsewhere],
            await ListClearancesAsync(scenario.OtherOrganizationId, null, scenario.Elsewhere.Id));

        Assert.Empty(await ListClearancesAsync(scenario.OrganizationId, null, scenario.Elsewhere.Id));
        Assert.Empty(await ListClearancesAsync(scenario.OrganizationId, scenario.ReaderId, scenario.Elsewhere.Id));
    }

    [LocalDbFact]
    public async Task ListClearances_OnSqlServer_OrganizationWithoutClearances_ReturnsEmpty()
    {
        await SeedAsync();

        Assert.Empty(await ListClearancesAsync(Guid.NewGuid(), null, null));
    }

    [LocalDbFact]
    public async Task ListClearances_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedAsync();

        await AssertTracksNothingAsync<IClearanceQueries>(
            queries => queries.ListAsync(scenario.OrganizationId, scenario.ReaderId, scenario.First.Id, CancellationToken.None));
    }

    // --- IClearanceQueries.FindAsync ---

    [LocalDbFact]
    public async Task FindClearance_OnSqlServer_ClearanceOnAPolicyOfTheOrganization_ReturnsItWithItsLevel()
    {
        var scenario = await SeedAsync();

        var clearance = await FindClearanceAsync(scenario.OrganizationId, scenario.OtherOnSecond.Id);

        Assert.Equal(scenario.OtherOnSecond, clearance);
    }

    // The clearance exists (control: its own organization finds it), only not on a policy of this organization
    [LocalDbFact]
    public async Task FindClearance_OnSqlServer_ClearanceOnAPolicyOfAnotherOrganization_ReturnsNull()
    {
        var scenario = await SeedAsync();
        Assert.Equal(scenario.ReaderElsewhere, await FindClearanceAsync(scenario.OtherOrganizationId, scenario.ReaderElsewhere.Id));

        Assert.Null(await FindClearanceAsync(scenario.OrganizationId, scenario.ReaderElsewhere.Id));
    }

    [LocalDbFact]
    public async Task FindClearance_OnSqlServer_MissingClearance_ReturnsNull()
    {
        var scenario = await SeedAsync();

        Assert.Null(await FindClearanceAsync(scenario.OrganizationId, Guid.NewGuid()));
    }

    [LocalDbFact]
    public async Task FindClearance_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedAsync();

        await AssertTracksNothingAsync<IClearanceQueries>(
            queries => queries.FindAsync(scenario.OrganizationId, scenario.ReaderOnFirst.Id, CancellationToken.None));
    }

    // --- helpers ---

    private Task<IReadOnlyList<PolicyEntry>> ListPoliciesAsync(Guid organizationId) =>
        WithQueriesAsync<IPolicyQueries, IReadOnlyList<PolicyEntry>>(
            queries => queries.ListAsync(organizationId, CancellationToken.None));

    private Task<PolicyEntry?> FindPolicyAsync(Guid organizationId, Guid policyId) =>
        WithQueriesAsync<IPolicyQueries, PolicyEntry?>(
            queries => queries.FindAsync(organizationId, policyId, CancellationToken.None));

    private Task<IReadOnlyList<ClearanceEntry>> ListClearancesAsync(Guid organizationId, Guid? userId, Guid? policyId) =>
        WithQueriesAsync<IClearanceQueries, IReadOnlyList<ClearanceEntry>>(
            queries => queries.ListAsync(organizationId, userId, policyId, CancellationToken.None));

    private Task<ClearanceEntry?> FindClearanceAsync(Guid organizationId, Guid clearanceId) =>
        WithQueriesAsync<IClearanceQueries, ClearanceEntry?>(
            queries => queries.FindAsync(organizationId, clearanceId, CancellationToken.None));

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

    // The organization has three policies, inserted Third, First, Second. Two users hold four clearances on First and
    // Second, inserted so the order by name (a, b, m, z) differs from the order of insertion and from the policy
    // order. Another organization has one policy, on which the reader holds a clearance too
    private async Task<Scenario> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();

        var third = NewPolicy(ThirdPrefix + suffix, organizationId);
        var first = NewPolicy(FirstPrefix + suffix, organizationId);
        var second = NewPolicy(SecondPrefix + suffix, organizationId);
        var elsewhere = NewPolicy(ElsewherePrefix + suffix, otherOrganizationId);
        var reader = NewUser();
        var other = NewUser();
        db.AddRange(third, first, second, elsewhere, reader, other);

        var otherOnFirst = NewClearance(ThirdPrefix + suffix, other.Id, first.Id, HighLevelName, HighLevelPriority);
        var readerOnSecond = NewClearance(SecondPrefix + suffix, reader.Id, second.Id, HighLevelName, HighLevelPriority);
        var readerOnFirst = NewClearance(FirstPrefix + suffix, reader.Id, first.Id, LowLevelName, LowLevelPriority);
        var otherOnSecond = NewClearance(FourthPrefix + suffix, other.Id, second.Id, LowLevelName, LowLevelPriority);
        var readerElsewhere = NewClearance(ElsewherePrefix + suffix, reader.Id, elsewhere.Id, LowLevelName, LowLevelPriority);
        db.Clearances.AddRange(otherOnFirst, readerOnSecond, readerOnFirst, otherOnSecond, readerElsewhere);
        await db.SaveChangesAsync();

        return new Scenario(
            organizationId,
            otherOrganizationId,
            reader.Id,
            new PolicyRow(first.Id, first.Name),
            new PolicyRow(second.Id, second.Name),
            new PolicyRow(third.Id, third.Name),
            new PolicyRow(elsewhere.Id, elsewhere.Name),
            Expected(readerOnFirst),
            Expected(otherOnSecond),
            Expected(readerOnSecond),
            Expected(otherOnFirst),
            Expected(readerElsewhere));
    }

    // What the read side must answer for a stored clearance, built from the values seeded, not read back
    private static ClearanceEntry Expected(DbClearance clearance) =>
        new(clearance.Id, clearance.Name, clearance.UserId, clearance.PolicyId,
            clearance.ClassificationLevel.Name, clearance.ClassificationLevel.Priority);

    private static DbUser NewUser() =>
        new() { Id = Guid.NewGuid(), Email = Guid.NewGuid().ToString("N") + EmailDomain, HashedPassword = HashedPassword };

    private static DbPolicy NewPolicy(string name, Guid organizationId) =>
        new() { Id = Guid.NewGuid(), Name = name, OrganizationId = organizationId };

    private static DbClearance NewClearance(string name, Guid userId, Guid policyId, string levelName, int levelPriority) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            UserId = userId,
            PolicyId = policyId,
            ClassificationLevel = new DbClassificationLevel { Name = levelName, Priority = levelPriority }
        };

    private sealed record PolicyRow(Guid Id, string Name);

    private sealed record Scenario(
        Guid OrganizationId,
        Guid OtherOrganizationId,
        Guid ReaderId,
        PolicyRow First,
        PolicyRow Second,
        PolicyRow Third,
        PolicyRow Elsewhere,
        ClearanceEntry ReaderOnFirst,
        ClearanceEntry OtherOnSecond,
        ClearanceEntry ReaderOnSecond,
        ClearanceEntry OtherOnFirst,
        ClearanceEntry ReaderElsewhere);
}

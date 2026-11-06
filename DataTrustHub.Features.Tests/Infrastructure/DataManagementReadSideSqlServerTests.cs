using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.ClassificationOptions;
using DataTrustHub.Domain.ReadModels.DataItems;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #74 AC6 on SQL Server itself (a throwaway LocalDB database built by the migrations; skipped where LocalDB is
// missing): each method of the DataManagement read side translates and runs in the database, applies the read rule
// (D3') and the delete rule (D5') there; the *_TracksNothing tests are regression guards (see AssertTracksNothingAsync),
// not proof of AsNoTracking. One scenario per test,
// with fresh users, organizations and policies, so the tests share the database without seeing each other's rows.
public sealed class DataManagementReadSideSqlServerTests(LocalDbApiFactory factory) : IClassFixture<LocalDbApiFactory>
{
    private const string EmailDomain = "@readside.test";
    private const string HashedPassword = "not-a-real-hash";
    private const string PersonalMarking = "UNCLASSIFIED";
    private const string ClassifiedMarking = "Classified";
    private const string ClearanceName = "Read side clearance";
    private const string Content = "cmVhZC1zaWRl";
    private const long Size = 12;
    private const int Internal = 1;
    private const int Confidential = 2;
    private const int Secret = 3;

    // Item names, chosen so the order by name differs from the order of insertion
    private const string OwnPersonalName = "a own personal";
    private const string ClearedName = "b cleared confidential";
    private const string AdministeredName = "c administered internal";
    private const string AboveLevelName = "d above level";
    private const string OwnDeletedName = "e own deleted";
    private const string OthersPersonalName = "f others personal";
    private const string DeletedClassifiedName = "g deleted classified";
    private const string GonePolicyName = "h policy gone";

    // --- ListReadableAsync ---

    [LocalDbFact]
    public async Task ListReadable_OnSqlServer_ReturnsOnlyReadableItemsOrderedByName()
    {
        var scenario = await SeedAsync();

        var entries = await WithQueriesAsync<IDataItemQueries, IReadOnlyList<DataItemListEntry>>(
            queries => queries.ListReadableAsync(scenario.ReaderId, CancellationToken.None));

        Assert.Equal([OwnPersonalName, ClearedName, AdministeredName], entries.Select(entry => entry.Name));
    }

    [LocalDbFact]
    public async Task ListReadable_OnSqlServer_ProjectsEveryFieldOfEachEntry()
    {
        var scenario = await SeedAsync();

        var entries = await WithQueriesAsync<IDataItemQueries, IReadOnlyList<DataItemListEntry>>(
            queries => queries.ListReadableAsync(scenario.ReaderId, CancellationToken.None));

        Assert.Equal(
            [
                new DataItemListEntry(scenario.OwnPersonalId, OwnPersonalName, Size, PersonalMarking, null, null, null,
                    scenario.ReaderEmail, IsOwner: true, CanDelete: true),
                new DataItemListEntry(scenario.ClearedId, ClearedName, Size, ClassifiedMarking, Confidential,
                    scenario.AlphaOmegaPolicyId, scenario.AlphaOmegaPolicyName, scenario.OwnerEmail, IsOwner: false, CanDelete: false),
                new DataItemListEntry(scenario.AdministeredId, AdministeredName, Size, ClassifiedMarking, Internal,
                    scenario.BetaPolicyId, scenario.BetaPolicyName, scenario.OwnerEmail, IsOwner: false, CanDelete: true)
            ],
            entries);
    }

    [LocalDbFact]
    public async Task ListReadable_OnSqlServer_UserWithoutItemsOrClearances_ReturnsEmpty()
    {
        await SeedAsync();
        var stranger = await SeedUserAsync();

        var entries = await WithQueriesAsync<IDataItemQueries, IReadOnlyList<DataItemListEntry>>(
            queries => queries.ListReadableAsync(stranger.Id, CancellationToken.None));

        Assert.Empty(entries);
    }

    [LocalDbFact]
    public async Task ListReadable_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedAsync();

        await AssertTracksNothingAsync<IDataItemQueries>(queries => queries.ListReadableAsync(scenario.ReaderId, CancellationToken.None));
    }

    // --- FindReadableAsync ---

    [LocalDbFact]
    public async Task FindReadable_OnSqlServer_ClassifiedItem_ReturnsContentPolicyAndItsOrganization()
    {
        var scenario = await SeedAsync();

        var item = await FindReadableAsync(scenario.ClearedId, scenario.ReaderId);

        Assert.Equal(
            new ReadableDataItem(scenario.ClearedId, ClearedName, Size, ClassifiedMarking, Content, Confidential,
                scenario.AlphaOmegaPolicyName, scenario.AlphaId, IsOwner: false, CanDelete: false),
            item);
    }

    [LocalDbFact]
    public async Task FindReadable_OnSqlServer_OwnPersonalItem_ReturnsItWithoutPolicyOrOrganization()
    {
        var scenario = await SeedAsync();

        var item = await FindReadableAsync(scenario.OwnPersonalId, scenario.ReaderId);

        Assert.Equal(
            new ReadableDataItem(scenario.OwnPersonalId, OwnPersonalName, Size, PersonalMarking, Content, null, null, null,
                IsOwner: true, CanDelete: true),
            item);
    }

    [LocalDbFact]
    public async Task FindReadable_OnSqlServer_OrganizationAdminWithClearance_CanDelete()
    {
        var scenario = await SeedAsync();

        var item = await FindReadableAsync(scenario.AdministeredId, scenario.ReaderId);

        Assert.True(item!.CanDelete);
        Assert.Equal(scenario.BetaId, item.OrganizationId);
    }

    [LocalDbFact]
    public async Task FindReadable_OnSqlServer_ClearanceBelowLevel_ReturnsNull()
    {
        var scenario = await SeedAsync();

        Assert.Null(await FindReadableAsync(scenario.AboveLevelId, scenario.ReaderId));
    }

    [LocalDbFact]
    public async Task FindReadable_OnSqlServer_OwnDeletedItem_ReturnsNull()
    {
        var scenario = await SeedAsync();

        Assert.Null(await FindReadableAsync(scenario.OwnDeletedId, scenario.ReaderId));
    }

    [LocalDbFact]
    public async Task FindReadable_OnSqlServer_AnotherUsersPersonalItem_ReturnsNull()
    {
        var scenario = await SeedAsync();

        Assert.Null(await FindReadableAsync(scenario.OthersPersonalId, scenario.ReaderId));
    }

    [LocalDbFact]
    public async Task FindReadable_OnSqlServer_MissingItem_ReturnsNull()
    {
        var scenario = await SeedAsync();

        Assert.Null(await FindReadableAsync(Guid.NewGuid(), scenario.ReaderId));
    }

    [LocalDbFact]
    public async Task FindReadable_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedAsync();

        await AssertTracksNothingAsync<IDataItemQueries>(
            queries => queries.FindReadableAsync(scenario.ClearedId, scenario.ReaderId, CancellationToken.None));
    }

    // --- FindDeniedReadTargetAsync ---

    [LocalDbFact]
    public async Task FindDeniedReadTarget_OnSqlServer_HiddenClassifiedItem_ReturnsItsPolicysOrganization()
    {
        var scenario = await SeedAsync();

        Assert.Equal(new DeniedReadTarget(scenario.AlphaId), await FindDeniedReadTargetAsync(scenario.AboveLevelId));
    }

    [LocalDbFact]
    public async Task FindDeniedReadTarget_OnSqlServer_DeletedClassifiedItem_ReturnsItsPolicysOrganization()
    {
        var scenario = await SeedAsync();

        Assert.Equal(new DeniedReadTarget(scenario.AlphaId), await FindDeniedReadTargetAsync(scenario.DeletedClassifiedId));
    }

    [LocalDbFact]
    public async Task FindDeniedReadTarget_OnSqlServer_DeletedPersonalItem_ReturnsATargetWithoutOrganization()
    {
        var scenario = await SeedAsync();

        Assert.Equal(new DeniedReadTarget(null), await FindDeniedReadTargetAsync(scenario.OwnDeletedId));
    }

    [LocalDbFact]
    public async Task FindDeniedReadTarget_OnSqlServer_PolicyNoLongerExists_ReturnsATargetWithoutOrganization()
    {
        var scenario = await SeedAsync();

        Assert.Equal(new DeniedReadTarget(null), await FindDeniedReadTargetAsync(scenario.GonePolicyItemId));
    }

    [LocalDbFact]
    public async Task FindDeniedReadTarget_OnSqlServer_MissingItem_ReturnsNull()
    {
        await SeedAsync();

        Assert.Null(await FindDeniedReadTargetAsync(Guid.NewGuid()));
    }

    [LocalDbFact]
    public async Task FindDeniedReadTarget_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedAsync();

        await AssertTracksNothingAsync<IDataItemQueries>(
            queries => queries.FindDeniedReadTargetAsync(scenario.AboveLevelId, CancellationToken.None));
    }

    // --- ListClearedPoliciesAsync ---

    [LocalDbFact]
    public async Task ListClearedPolicies_OnSqlServer_ReturnsEachClearedPolicyByOrganizationThenPolicyName()
    {
        var scenario = await SeedAsync();

        var policies = await WithQueriesAsync<IClassificationOptionQueries, IReadOnlyList<ClearedPolicy>>(
            queries => queries.ListClearedPoliciesAsync(scenario.ReaderId, CancellationToken.None));

        Assert.Equal(
            [
                new ClearedPolicy(scenario.AlphaAlphaPolicyId, scenario.AlphaAlphaPolicyName, scenario.AlphaName, Internal),
                new ClearedPolicy(scenario.AlphaOmegaPolicyId, scenario.AlphaOmegaPolicyName, scenario.AlphaName, Confidential),
                new ClearedPolicy(scenario.BetaPolicyId, scenario.BetaPolicyName, scenario.BetaName, Secret)
            ],
            policies);
    }

    [LocalDbFact]
    public async Task ListClearedPolicies_OnSqlServer_UserWithoutClearances_ReturnsEmpty()
    {
        await SeedAsync();
        var stranger = await SeedUserAsync();

        var policies = await WithQueriesAsync<IClassificationOptionQueries, IReadOnlyList<ClearedPolicy>>(
            queries => queries.ListClearedPoliciesAsync(stranger.Id, CancellationToken.None));

        Assert.Empty(policies);
    }

    [LocalDbFact]
    public async Task ListClearedPolicies_OnSqlServer_TracksNothing()
    {
        var scenario = await SeedAsync();

        await AssertTracksNothingAsync<IClassificationOptionQueries>(
            queries => queries.ListClearedPoliciesAsync(scenario.ReaderId, CancellationToken.None));
    }

    // --- helpers ---

    private Task<ReadableDataItem?> FindReadableAsync(Guid itemId, Guid requesterId) =>
        WithQueriesAsync<IDataItemQueries, ReadableDataItem?>(
            queries => queries.FindReadableAsync(itemId, requesterId, CancellationToken.None));

    private Task<DeniedReadTarget?> FindDeniedReadTargetAsync(Guid itemId) =>
        WithQueriesAsync<IDataItemQueries, DeniedReadTarget?>(
            queries => queries.FindDeniedReadTargetAsync(itemId, CancellationToken.None));

    // The port as a request gets it: resolved from a new scope of the API container
    private async Task<TResult> WithQueriesAsync<TPort, TResult>(Func<TPort, Task<TResult>> read)
        where TPort : notnull
    {
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<TPort>());
    }

    // A regression guard only: the request's context holds no entity after the read. Every method projects to a
    // record, and EF never tracks a non-entity projection, so this would still pass without AsNoTracking and does not
    // prove "no entity loading" either. It fails if a method starts returning or materializing tracked entities. The
    // AC6 evidence is the tests above that run each method on SQL Server (translation, server-side read and delete rule)
    private async Task AssertTracksNothingAsync<TPort>(Func<TPort, Task> read)
        where TPort : notnull
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();

        await read(scope.ServiceProvider.GetRequiredService<TPort>());

        Assert.Empty(db.ChangeTracker.Entries());
    }

    // The reader holds clearances on three policies in two organizations and is an organization admin of Beta; the
    // owner uploaded the classified items. Alpha's policies are named so their order differs from insertion
    private async Task<Scenario> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var suffix = Guid.NewGuid().ToString("N");

        var reader = NewUser();
        var owner = NewUser();
        var beta = new DbOrganization { Id = Guid.NewGuid(), Name = "Beta " + suffix };
        var alpha = new DbOrganization { Id = Guid.NewGuid(), Name = "Alpha " + suffix };
        var betaPolicy = NewPolicy("Zeta " + suffix, beta.Id);
        var alphaOmega = NewPolicy("Omega " + suffix, alpha.Id);
        var alphaAlpha = NewPolicy("Alpha " + suffix, alpha.Id);
        db.AddRange(reader, owner, beta, alpha, betaPolicy, alphaOmega, alphaAlpha);
        db.Clearances.AddRange(
            NewClearance(reader.Id, betaPolicy.Id, Secret),
            NewClearance(reader.Id, alphaOmega.Id, Confidential),
            NewClearance(reader.Id, alphaAlpha.Id, Internal));
        db.Memberships.Add(new DbMembership
        {
            Id = Guid.NewGuid(), UserId = reader.Id, OrganizationId = beta.Id, Role = MembershipRole.OrgAdmin.Name
        });

        var gonePolicy = NewPolicy("Gone " + suffix, alpha.Id);
        var items = new Dictionary<string, DbDataItem>
        {
            [OthersPersonalName] = NewItem(OthersPersonalName, owner.Id, null, null, false),
            [AdministeredName] = NewItem(AdministeredName, owner.Id, betaPolicy.Id, Internal, false),
            [AboveLevelName] = NewItem(AboveLevelName, owner.Id, alphaOmega.Id, Secret, false),
            [ClearedName] = NewItem(ClearedName, owner.Id, alphaOmega.Id, Confidential, false),
            [OwnDeletedName] = NewItem(OwnDeletedName, reader.Id, null, null, true),
            [OwnPersonalName] = NewItem(OwnPersonalName, reader.Id, null, null, false),
            [DeletedClassifiedName] = NewItem(DeletedClassifiedName, owner.Id, alphaOmega.Id, Internal, true),
            // The policy is never stored: an item whose policy no longer exists
            [GonePolicyName] = NewItem(GonePolicyName, owner.Id, gonePolicy.Id, Internal, false)
        };
        db.DataItems.AddRange(items.Values);
        await db.SaveChangesAsync();

        return new Scenario(
            reader.Id, reader.Email, owner.Email,
            alpha.Id, alpha.Name, beta.Id, beta.Name,
            alphaAlpha.Id, alphaAlpha.Name, alphaOmega.Id, alphaOmega.Name, betaPolicy.Id, betaPolicy.Name,
            items[OwnPersonalName].Id, items[ClearedName].Id, items[AdministeredName].Id, items[AboveLevelName].Id,
            items[OwnDeletedName].Id, items[OthersPersonalName].Id, items[DeletedClassifiedName].Id, items[GonePolicyName].Id);
    }

    private async Task<DbUser> SeedUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var user = NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static DbUser NewUser() =>
        new() { Id = Guid.NewGuid(), Email = Guid.NewGuid().ToString("N") + EmailDomain, HashedPassword = HashedPassword };

    private static DbPolicy NewPolicy(string name, Guid organizationId) =>
        new() { Id = Guid.NewGuid(), Name = name, OrganizationId = organizationId };

    private static DbClearance NewClearance(Guid userId, Guid policyId, int priority) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = ClearanceName,
            UserId = userId,
            PolicyId = policyId,
            ClassificationLevel = new DbClassificationLevel { Name = ClearanceName, Priority = priority }
        };

    private static DbDataItem NewItem(string name, Guid ownerId, Guid? policyId, int? priority, bool isDeleted) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Size = Size,
            Content = Content,
            OwnerUserId = ownerId,
            SecurityMarking = policyId is null ? PersonalMarking : ClassifiedMarking,
            PolicyId = policyId,
            ClassificationPriority = priority,
            IsDeleted = isDeleted
        };

    private sealed record Scenario(
        Guid ReaderId,
        string ReaderEmail,
        string OwnerEmail,
        Guid AlphaId,
        string AlphaName,
        Guid BetaId,
        string BetaName,
        Guid AlphaAlphaPolicyId,
        string AlphaAlphaPolicyName,
        Guid AlphaOmegaPolicyId,
        string AlphaOmegaPolicyName,
        Guid BetaPolicyId,
        string BetaPolicyName,
        Guid OwnPersonalId,
        Guid ClearedId,
        Guid AdministeredId,
        Guid AboveLevelId,
        Guid OwnDeletedId,
        Guid OthersPersonalId,
        Guid DeletedClassifiedId,
        Guid GonePolicyItemId);
}

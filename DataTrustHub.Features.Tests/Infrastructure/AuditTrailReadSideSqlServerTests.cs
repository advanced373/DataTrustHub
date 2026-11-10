using DataTrustHub.Domain.Auditing;
using DataTrustHub.Domain.ReadModels.Auditing;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #74 part 4e, AC5 on SQL Server itself (a throwaway LocalDB database built by the migrations; skipped where LocalDB is
// missing): both methods of the audit trail read side translate and run in the database as a projection, with their
// scope, every filter (combined with AND, From and To inclusive), the order (newest first, the id as tie-break), the
// cut at the maximum and the actor's email by left join (null for an anonymous action or a deleted account). Most
// scenarios run through both methods: each test's records carry its own actions and its own organization, so either
// scope selects exactly them, and the tests share the database without seeing each other's rows
public sealed class AuditTrailReadSideSqlServerTests(LocalDbApiFactory factory) : IClassFixture<LocalDbApiFactory>
{
    private const string EmailDomain = "@readside-audit.test";
    private const string HashedPassword = "not-a-real-hash";
    private const string ActionPrefix = "ReadSideAudit.";
    private const string TargetType = "DataItem";
    private const string ErrorCode = "DataItem.NotFound";
    private const int LastGuidByte = 15;
    private const int SmallMax = 3;
    private const int BigMax = 200;

    private static readonly DateTime BaseTime = new(2026, 11, 2, 9, 15, 30, DateTimeKind.Utc);

    public enum Scope
    {
        Actions,
        Organization
    }

    // --- scope ---

    [LocalDbFact]
    public async Task ListForActions_OnSqlServer_ReturnsOnlyTheGivenActions()
    {
        var trail = NewTrail();
        var outside = NewAction();
        var first = trail.Record(trail.Action, BaseTime);
        var second = trail.Record(trail.OtherAction, BaseTime.AddMinutes(1));
        var notAsked = trail.Record(outside, BaseTime.AddMinutes(2));
        await SeedAsync(first, second, notAsked);

        var entries = await ListAsync(Scope.Actions, trail, EmptyFilter, BigMax);

        Assert.Equal([second.Id, first.Id], entries.Select(entry => entry.Id));
    }

    // The same records are in the organization trail (control), and none belongs to another organization or to none
    [LocalDbFact]
    public async Task ListForOrganization_OnSqlServer_ReturnsOnlyThatOrganizationsRecords()
    {
        var trail = NewTrail();
        var mine = trail.Record(trail.Action, BaseTime);
        var otherOrganization = Trail.RecordIn(Guid.NewGuid(), trail.Action, BaseTime.AddMinutes(1));
        var noOrganization = Trail.RecordIn(null, trail.Action, BaseTime.AddMinutes(2));
        await SeedAsync(mine, otherOrganization, noOrganization);

        var entries = await ListAsync(Scope.Organization, trail, EmptyFilter, BigMax);

        Assert.Equal([mine.Id], entries.Select(entry => entry.Id));
        Assert.Equal(3, (await ListAsync(Scope.Actions, trail, EmptyFilter, BigMax)).Count);
    }

    // --- the projection ---

    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_ProjectsEveryFieldWithOccurredAtInUtc(Scope scope)
    {
        var trail = NewTrail();
        var actor = await SeedUserAsync();
        var succeeded = trail.Record(trail.Action, BaseTime, actor.Id, targetId: Guid.NewGuid());
        var failed = trail.Record(trail.OtherAction, BaseTime.AddMinutes(-1), outcome: AuditOutcome.Failed, errorCode: ErrorCode);
        await SeedAsync(succeeded, failed);

        var entries = await ListAsync(scope, trail, EmptyFilter, BigMax);

        Assert.Equal([Expected(succeeded, actor.Email), Expected(failed, null)], entries);
        Assert.All(entries, entry => Assert.Equal(DateTimeKind.Utc, entry.OccurredAt.Kind));
    }

    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_NothingInScope_ReturnsEmpty(Scope scope)
    {
        Assert.Empty(await ListAsync(scope, NewTrail(), EmptyFilter, BigMax));
    }

    // --- the filters ---

    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_ActorFilter_ReturnsOnlyThatActorsRecords(Scope scope)
    {
        var trail = NewTrail();
        var actorId = Guid.NewGuid();
        var mine = trail.Record(trail.Action, BaseTime, actorId);
        var someoneElses = trail.Record(trail.Action, BaseTime.AddMinutes(1), Guid.NewGuid());
        var anonymous = trail.Record(trail.Action, BaseTime.AddMinutes(2));
        await SeedAsync(mine, someoneElses, anonymous);

        var entries = await ListAsync(scope, trail, new AuditRecordFilter(actorId, null, null, null), BigMax);

        Assert.Equal([mine.Id], entries.Select(entry => entry.Id));
    }

    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_ActionFilter_IsAnExactMatch(Scope scope)
    {
        var trail = NewTrail();
        var match = trail.Record(trail.Action, BaseTime);
        var other = trail.Record(trail.OtherAction, BaseTime.AddMinutes(1));
        await SeedAsync(match, other);

        var entries = await ListAsync(scope, trail, new AuditRecordFilter(null, trail.Action, null, null), BigMax);
        var prefix = await ListAsync(scope, trail, new AuditRecordFilter(null, trail.Action[..^1], null, null), BigMax);

        Assert.Equal([match.Id], entries.Select(entry => entry.Id));
        Assert.Empty(prefix);
    }

    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_FromFilter_IsInclusive(Scope scope)
    {
        var trail = NewTrail();
        var before = trail.Record(trail.Action, BaseTime.AddSeconds(-1));
        var atFrom = trail.Record(trail.Action, BaseTime);
        var after = trail.Record(trail.Action, BaseTime.AddSeconds(1));
        await SeedAsync(before, atFrom, after);

        var entries = await ListAsync(scope, trail, new AuditRecordFilter(null, null, BaseTime, null), BigMax);

        Assert.Equal([after.Id, atFrom.Id], entries.Select(entry => entry.Id));
    }

    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_ToFilter_IsInclusive(Scope scope)
    {
        var trail = NewTrail();
        var before = trail.Record(trail.Action, BaseTime.AddSeconds(-1));
        var atTo = trail.Record(trail.Action, BaseTime);
        var after = trail.Record(trail.Action, BaseTime.AddSeconds(1));
        await SeedAsync(before, atTo, after);

        var entries = await ListAsync(scope, trail, new AuditRecordFilter(null, null, null, BaseTime), BigMax);

        Assert.Equal([atTo.Id, before.Id], entries.Select(entry => entry.Id));
    }

    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_FromEqualsTo_ReturnsTheRecordsAtThatInstant(Scope scope)
    {
        var trail = NewTrail();
        var before = trail.Record(trail.Action, BaseTime.AddSeconds(-1));
        var at = trail.Record(trail.Action, BaseTime);
        var after = trail.Record(trail.Action, BaseTime.AddSeconds(1));
        await SeedAsync(before, at, after);

        var entries = await ListAsync(scope, trail, new AuditRecordFilter(null, null, BaseTime, BaseTime), BigMax);

        Assert.Equal([at.Id], entries.Select(entry => entry.Id));
    }

    // Each record fails exactly one filter, so only the one that passes them all is returned
    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_AllFiltersCombined_AreAnded(Scope scope)
    {
        var trail = NewTrail();
        var actorId = Guid.NewGuid();
        var from = BaseTime;
        var to = BaseTime.AddHours(1);
        var match = trail.Record(trail.Action, BaseTime.AddMinutes(30), actorId);
        var otherActor = trail.Record(trail.Action, BaseTime.AddMinutes(30), Guid.NewGuid());
        var otherAction = trail.Record(trail.OtherAction, BaseTime.AddMinutes(30), actorId);
        var tooEarly = trail.Record(trail.Action, from.AddSeconds(-1), actorId);
        var tooLate = trail.Record(trail.Action, to.AddSeconds(1), actorId);
        await SeedAsync(match, otherActor, otherAction, tooEarly, tooLate);

        var entries = await ListAsync(scope, trail, new AuditRecordFilter(actorId, trail.Action, from, to), BigMax);

        Assert.Equal([match.Id], entries.Select(entry => entry.Id));
    }

    // The filter narrows the scope, it does not widen it: an action outside the asked actions finds nothing
    [LocalDbFact]
    public async Task ListForActions_OnSqlServer_ActionFilterOutsideTheGivenActions_ReturnsNothing()
    {
        var trail = NewTrail();
        var outside = NewAction();
        await SeedAsync(trail.Record(trail.Action, BaseTime), trail.Record(outside, BaseTime));

        var entries = await ListAsync(Scope.Actions, trail, new AuditRecordFilter(null, outside, null, null), BigMax);

        Assert.Empty(entries);
    }

    // --- the order and the cut ---

    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_ReturnsNewestFirstThenByIdDescending(Scope scope)
    {
        var trail = NewTrail();
        var tieBytes = Guid.NewGuid().ToByteArray();
        var oldest = trail.Record(trail.Action, BaseTime.AddMinutes(-5));
        var tiedLow = trail.Record(trail.Action, BaseTime, id: WithLastByte(tieBytes, 0x01));
        var tiedHigh = trail.Record(trail.Action, BaseTime, id: WithLastByte(tieBytes, 0x02));
        var newest = trail.Record(trail.Action, BaseTime.AddMinutes(5));
        await SeedAsync(tiedLow, oldest, newest, tiedHigh);

        var entries = await ListAsync(scope, trail, EmptyFilter, BigMax);

        Assert.Equal([newest.Id, tiedHigh.Id, tiedLow.Id, oldest.Id], entries.Select(entry => entry.Id));
    }

    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_MoreThanTheMaximum_ReturnsTheNewestUpToIt(Scope scope)
    {
        var trail = NewTrail();
        var records = Enumerable.Range(0, SmallMax + 2)
            .Select(minute => trail.Record(trail.Action, BaseTime.AddMinutes(minute)))
            .ToArray();
        await SeedAsync(records.Reverse().ToArray());

        var entries = await ListAsync(scope, trail, EmptyFilter, SmallMax);

        Assert.Equal(
            records.OrderByDescending(record => record.OccurredAt).Take(SmallMax).Select(record => record.Id),
            entries.Select(entry => entry.Id));
    }

    // All at the same instant: the cut keeps the highest ids, so it is stable
    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_MoreThanTheMaximumAtTheSameTime_CutsTheLowestIds(Scope scope)
    {
        var trail = NewTrail();
        var tieBytes = Guid.NewGuid().ToByteArray();
        var records = Enumerable.Range(1, SmallMax + 2)
            .Select(last => trail.Record(trail.Action, BaseTime, id: WithLastByte(tieBytes, (byte)last)))
            .ToArray();
        await SeedAsync(records);

        var entries = await ListAsync(scope, trail, EmptyFilter, SmallMax);

        Assert.Equal(
            records.Reverse().Take(SmallMax).Select(record => record.Id),
            entries.Select(entry => entry.Id));
    }

    // The cut applies after the filters, so a filter can reach records the unfiltered cut would leave out
    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_CutComesAfterTheFilters(Scope scope)
    {
        var trail = NewTrail();
        var old = trail.Record(trail.OtherAction, BaseTime.AddDays(-1));
        var recent = Enumerable.Range(0, SmallMax)
            .Select(minute => trail.Record(trail.Action, BaseTime.AddMinutes(minute)))
            .ToArray();
        await SeedAsync([old, .. recent]);

        var entries = await ListAsync(scope, trail, new AuditRecordFilter(null, trail.OtherAction, null, null), SmallMax);

        Assert.Equal([old.Id], entries.Select(entry => entry.Id));
    }

    // --- the actor's email by left join ---

    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_KnownActor_ReturnsTheActorsEmail(Scope scope)
    {
        var trail = NewTrail();
        var actor = await SeedUserAsync();
        await SeedAsync(trail.Record(trail.Action, BaseTime, actor.Id));

        var entry = Assert.Single(await ListAsync(scope, trail, EmptyFilter, BigMax));

        Assert.Equal((actor.Id, actor.Email), (entry.ActorUserId, entry.ActorEmail));
    }

    // The left join keeps the record: an anonymous action has no actor and no email
    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_AnonymousActor_KeepsTheRecordWithNoEmail(Scope scope)
    {
        var trail = NewTrail();
        var record = trail.Record(trail.Action, BaseTime);
        await SeedAsync(record);

        var entry = Assert.Single(await ListAsync(scope, trail, EmptyFilter, BigMax));

        Assert.Equal((record.Id, (Guid?)null, (string?)null), (entry.Id, entry.ActorUserId, entry.ActorEmail));
    }

    // The account was deleted after it acted: the record keeps the actor's id and has no email
    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_ActorWhoseAccountWasDeleted_KeepsTheRecordWithNoEmail(Scope scope)
    {
        var trail = NewTrail();
        var actor = await SeedUserAsync();
        var record = trail.Record(trail.Action, BaseTime, actor.Id);
        await SeedAsync(record);
        await DeleteUserAsync(actor.Id);

        var entry = Assert.Single(await ListAsync(scope, trail, EmptyFilter, BigMax));

        Assert.Equal((record.Id, (Guid?)actor.Id, (string?)null), (entry.Id, entry.ActorUserId, entry.ActorEmail));
    }

    // Known, anonymous and deleted actors together: one row per record, each with its own email or none
    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_MixedActors_ReturnOneEntryPerRecord(Scope scope)
    {
        var trail = NewTrail();
        var known = await SeedUserAsync();
        var deleted = await SeedUserAsync();
        var byKnown = trail.Record(trail.Action, BaseTime.AddMinutes(2), known.Id);
        var byKnownAgain = trail.Record(trail.Action, BaseTime.AddMinutes(1), known.Id);
        var anonymous = trail.Record(trail.Action, BaseTime);
        var byDeleted = trail.Record(trail.Action, BaseTime.AddMinutes(-1), deleted.Id);
        await SeedAsync(byKnown, byKnownAgain, anonymous, byDeleted);
        await DeleteUserAsync(deleted.Id);

        var entries = await ListAsync(scope, trail, EmptyFilter, BigMax);

        Assert.Equal(
            [(byKnown.Id, known.Email), (byKnownAgain.Id, known.Email), (anonymous.Id, null), (byDeleted.Id, null)],
            entries.Select(entry => (entry.Id, entry.ActorEmail)));
    }

    // --- tracking ---

    // A regression guard only: the request's context holds no entity after the read. Both methods project to a record,
    // and EF never tracks a non-entity projection, so this would still pass without AsNoTracking
    [LocalDbTheory]
    [InlineData(Scope.Actions)]
    [InlineData(Scope.Organization)]
    public async Task List_OnSqlServer_TracksNothing(Scope scope)
    {
        var trail = NewTrail();
        var actor = await SeedUserAsync();
        await SeedAsync(trail.Record(trail.Action, BaseTime, actor.Id));

        using var serviceScope = factory.Services.CreateScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<DContext>();
        var queries = serviceScope.ServiceProvider.GetRequiredService<IAuditRecordQueries>();

        Assert.Single(await Read(queries, scope, trail, EmptyFilter, BigMax));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    // --- helpers ---

    private static readonly AuditRecordFilter EmptyFilter = new(null, null, null, null);

    // The port as a request gets it: resolved from a new scope of the API container
    private async Task<IReadOnlyList<AuditRecordEntry>> ListAsync(Scope scope, Trail trail, AuditRecordFilter filter, int max)
    {
        using var serviceScope = factory.Services.CreateScope();
        return await Read(serviceScope.ServiceProvider.GetRequiredService<IAuditRecordQueries>(), scope, trail, filter, max);
    }

    private static Task<IReadOnlyList<AuditRecordEntry>> Read(
        IAuditRecordQueries queries, Scope scope, Trail trail, AuditRecordFilter filter, int max) =>
        scope switch
        {
            Scope.Actions => queries.ListForActionsAsync(trail.Actions, filter, max, CancellationToken.None),
            Scope.Organization => queries.ListForOrganizationAsync(trail.OrganizationId, filter, max, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };

    private async Task SeedAsync(params DbAuditRecord[] records)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        db.AuditRecords.AddRange(records);
        await db.SaveChangesAsync();
    }

    private async Task<DbUser> SeedUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var user = new DbUser
        {
            Id = Guid.NewGuid(),
            Email = Guid.NewGuid().ToString("N") + EmailDomain,
            HashedPassword = HashedPassword,
            Role = UserRoles.User
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task DeleteUserAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        db.Users.Remove(db.Users.Single(user => user.Id == userId));
        await db.SaveChangesAsync();
    }

    private static Trail NewTrail() => new(NewAction(), NewAction(), Guid.NewGuid());

    // Unique per test, within the column's length
    private static string NewAction() => ActionPrefix + Guid.NewGuid().ToString("N");

    private static Guid WithLastByte(byte[] bytes, byte last)
    {
        var copy = (byte[])bytes.Clone();
        copy[LastGuidByte] = last;
        return new Guid(copy);
    }

    // What the read side must answer for a stored row, built from the values seeded, not read back
    private static AuditRecordEntry Expected(DbAuditRecord record, string? actorEmail) =>
        new(
            record.Id,
            record.OccurredAt,
            record.ActorUserId,
            actorEmail,
            record.Action,
            record.TargetType,
            record.TargetId,
            record.Outcome,
            record.ErrorCode);

    // One test's records: two actions of its own and an organization of its own, so either scope selects exactly them
    private sealed record Trail(string Action, string OtherAction, Guid OrganizationId)
    {
        internal IReadOnlyCollection<string> Actions => [Action, OtherAction];

        internal DbAuditRecord Record(
            string action,
            DateTime occurredAt,
            Guid? actorUserId = null,
            Guid? targetId = null,
            AuditOutcome outcome = AuditOutcome.Succeeded,
            string? errorCode = null,
            Guid? id = null) =>
            RecordIn(OrganizationId, action, occurredAt, actorUserId, targetId, outcome, errorCode, id);

        // A record in the given organization, or in none
        internal static DbAuditRecord RecordIn(
            Guid? organizationId,
            string action,
            DateTime occurredAt,
            Guid? actorUserId = null,
            Guid? targetId = null,
            AuditOutcome outcome = AuditOutcome.Succeeded,
            string? errorCode = null,
            Guid? id = null) =>
            new()
            {
                Id = id ?? Guid.NewGuid(),
                OccurredAt = occurredAt,
                ActorUserId = actorUserId,
                Action = action,
                TargetType = targetId is null ? null : TargetType,
                TargetId = targetId,
                OrganizationId = organizationId,
                Outcome = outcome.ToString(),
                ErrorCode = errorCode
            };
    }
}

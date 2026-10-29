using System.Net;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance.Model;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AuditTrailSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// #24 step 3 (ADR-0016): an organization admin reads their organization's audit trail. On the relational
// SQLite provider, so the filters, the left join to Users and the ordering are translated to SQL.
// Tests in a class run one after another, so counting the stored records around a request is exact
public class ListOrganizationAuditRecordsTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    private const string EmailDomain = "@example.com";

    // --- AC1: only this organization's records; the organization admin check runs first ---

    [Fact]
    public async Task ListOrganizationAuditRecords_OrgAdmin_ReturnsExactlyTheOrganizationsRecords()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_scope");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_scope_other");
        var here = Record(AddMemberAction, FutureTime, scenario.OrgAdminId, scenario.OrganizationId);
        var elsewhere = Record(AddMemberAction, FutureTime, scenario.OrgAdminId, other.OrganizationId);
        var withoutOrganization = Record(DeleteDataItemAction, FutureTime, scenario.OrgAdminId);
        await SeedAsync(factory, here, elsewhere, withoutOrganization);

        var records = await ReadAsync(await scenario.OrgAdmin.GetAsync(OrganizationAuditRecordsEndpoint(scenario.OrganizationId)));

        var expected = (await StoredRecordsAsync(factory)).Where(r => r.OrganizationId == scenario.OrganizationId).Select(r => r.Id);
        Assert.Equal(expected.ToHashSet(), records.Select(r => r.Id).ToHashSet());
        Assert.Contains(records, r => r.Id == here.Id);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_OrgAdmin_IncludesTheAppointmentOfTheOrganizationAdmin()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_appoint");

        var records = await ReadAsync(await scenario.OrgAdmin.GetAsync(
            OrganizationAuditRecordsEndpoint(scenario.OrganizationId) + Filters(action: AppointOrganizationAdminAction)));

        Assert.Single(records);
    }

    [Theory]
    [MemberData(nameof(NotOrgAdminKinds))]
    public async Task ListOrganizationAuditRecords_CallerNotOrgAdmin_Returns404EvenWhenRecordsExist(string kind)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"audit_org_not_admin_{kind}");
        await SeedAsync(factory, Record(AddMemberAction, FutureTime, scenario.OrgAdminId, scenario.OrganizationId));
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, kind, $"audit_org_not_admin_{kind}");

        var response = await caller.GetAsync(OrganizationAuditRecordsEndpoint(scenario.OrganizationId));

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);
    }

    // A10: a user never sees their own records through this route
    [Fact]
    public async Task ListOrganizationAuditRecords_MemberAskingForOwnRecords_Returns404()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_own");
        var (member, memberId) = await CreateUserAsync(factory, "audit_org_own_member");
        await AdminSetup.AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, memberId, MemberRole);
        await SeedAsync(factory, Record(DownloadDataItemAction, FutureTime, memberId, scenario.OrganizationId, AuditOutcome.Denied));

        var response = await member.GetAsync(OrganizationAuditRecordsEndpoint(scenario.OrganizationId) + Filters(userId: memberId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_UnknownOrganization_Returns404()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_unknown");

        var response = await scenario.OrgAdmin.GetAsync(OrganizationAuditRecordsEndpoint(Guid.NewGuid()));

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_Anonymous_Returns401()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_anonymous");

        var response = await factory.CreateClient().GetAsync(OrganizationAuditRecordsEndpoint(scenario.OrganizationId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- AC3: filters, each alone and combined with AND; from and to inclusive ---

    [Fact]
    public async Task ListOrganizationAuditRecords_UserIdFilter_ReturnsOnlyThatActorsRecords()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_user_filter");
        var actorId = Guid.NewGuid();
        var mine = Record(AddMemberAction, FutureTime, actorId, scenario.OrganizationId);
        await SeedAsync(factory, mine, Record(AddMemberAction, FutureTime, Guid.NewGuid(), scenario.OrganizationId));

        var records = await ListAsync(scenario, Filters(userId: actorId));

        Assert.Equal(mine.Id, Assert.Single(records).Id);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_ActionFilter_IsAnExactMatch()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_action_filter");
        var download = Record(DownloadDataItemAction, FutureTime, scenario.OrgAdminId, scenario.OrganizationId);
        var similar = Record(DownloadDataItemAction + "s", FutureTime, scenario.OrgAdminId, scenario.OrganizationId);
        await SeedAsync(factory, download, similar, Record(DeleteDataItemAction, FutureTime, scenario.OrgAdminId, scenario.OrganizationId));

        var records = await ListAsync(scenario, Filters(action: DownloadDataItemAction));

        Assert.Equal(download.Id, Assert.Single(records).Id);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_FromAndTo_AreInclusiveBounds()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_range");
        var (before, atFrom, inside, atTo, after) = await SeedRangeAsync(scenario.OrganizationId);

        var records = await ListAsync(scenario, Filters(from: atFrom.OccurredAt, to: atTo.OccurredAt));

        Assert.Equal(new HashSet<Guid> { atFrom.Id, inside.Id, atTo.Id }, records.Select(r => r.Id).ToHashSet());
        Assert.DoesNotContain(records, r => r.Id == before.Id || r.Id == after.Id);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_FromAlone_ReturnsRecordsAtOrAfterIt()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_from");
        var (_, atFrom, inside, atTo, after) = await SeedRangeAsync(scenario.OrganizationId);

        var records = await ListAsync(scenario, Filters(from: atFrom.OccurredAt));

        Assert.Equal(new HashSet<Guid> { atFrom.Id, inside.Id, atTo.Id, after.Id }, records.Select(r => r.Id).ToHashSet());
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_ToAlone_ExcludesRecordsAfterIt()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_to");
        var (before, atFrom, _, atTo, after) = await SeedRangeAsync(scenario.OrganizationId);

        var records = await ListAsync(scenario, Filters(to: atTo.OccurredAt));

        Assert.Contains(records, r => r.Id == before.Id);
        Assert.Contains(records, r => r.Id == atFrom.Id);
        Assert.Contains(records, r => r.Id == atTo.Id);
        Assert.DoesNotContain(records, r => r.Id == after.Id);
    }

    // A time without a zone is taken as UTC (the stored times are UTC)
    [Fact]
    public async Task ListOrganizationAuditRecords_UnzonedTimes_AreTakenAsUtc()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_unzoned");
        var (_, atFrom, inside, atTo, _) = await SeedRangeAsync(scenario.OrganizationId);
        var unzoned = $"?from={atFrom.OccurredAt:yyyy-MM-ddTHH:mm:ss}&to={atTo.OccurredAt:yyyy-MM-ddTHH:mm:ss}";

        var records = await ListAsync(scenario, unzoned);

        Assert.Equal(new HashSet<Guid> { atFrom.Id, inside.Id, atTo.Id }, records.Select(r => r.Id).ToHashSet());
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_AllFiltersCombined_AreAnded()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_combined");
        var actorId = Guid.NewGuid();
        var match = Record(DownloadDataItemAction, FutureTime.AddMinutes(1), actorId, scenario.OrganizationId);
        await SeedAsync(factory,
            match,
            Record(DeleteDataItemAction, FutureTime.AddMinutes(1), actorId, scenario.OrganizationId),
            Record(DownloadDataItemAction, FutureTime.AddMinutes(1), Guid.NewGuid(), scenario.OrganizationId),
            Record(DownloadDataItemAction, FutureTime.AddHours(1), actorId, scenario.OrganizationId));

        var records = await ListAsync(scenario, Filters(actorId, DownloadDataItemAction, FutureTime, FutureTime.AddMinutes(2)));

        Assert.Equal(match.Id, Assert.Single(records).Id);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_FromEqualsTo_Returns200()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_equal_bounds");
        var record = Record(AddMemberAction, FutureTime, scenario.OrgAdminId, scenario.OrganizationId);
        await SeedAsync(factory, record);

        var records = await ListAsync(scenario, Filters(from: FutureTime, to: FutureTime));

        Assert.Equal(record.Id, Assert.Single(records).Id);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_ToBeforeFrom_Returns400()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_inverted");

        var response = await scenario.OrgAdmin.GetAsync(
            OrganizationAuditRecordsEndpoint(scenario.OrganizationId) + Filters(from: FutureTime, to: FutureTime.AddSeconds(-1)));

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.BadRequest, AdminErrorCodes.ValidationGeneral, AdminErrorMessages.ValidationGeneral);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_ActionLongerThanTheColumn_Returns400()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_long_action");

        var response = await scenario.OrgAdmin.GetAsync(
            OrganizationAuditRecordsEndpoint(scenario.OrganizationId) + Filters(action: new string('a', ActionMaxLength + 1)));

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.BadRequest, AdminErrorCodes.ValidationGeneral, AdminErrorMessages.ValidationGeneral);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_ActionOfTheColumnsLength_Returns200()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_max_action");
        var record = Record(new string('a', ActionMaxLength), FutureTime, scenario.OrgAdminId, scenario.OrganizationId);
        await SeedAsync(factory, record);

        var records = await ListAsync(scenario, Filters(action: record.Action));

        Assert.Equal(record.Id, Assert.Single(records).Id);
    }

    // A non-admin with an invalid filter learns nothing either: the answer must not be a 200
    [Fact]
    public async Task ListOrganizationAuditRecords_OutsiderWithInvalidFilter_DoesNotSucceed()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_outsider_invalid");
        var (outsider, _) = await CreateUserAsync(factory, "audit_org_outsider_invalid_caller");

        var response = await outsider.GetAsync(
            OrganizationAuditRecordsEndpoint(scenario.OrganizationId) + Filters(from: FutureTime, to: FutureTime.AddSeconds(-1)));

        Assert.False(response.IsSuccessStatusCode);
    }

    // --- order and limit ---

    [Fact]
    public async Task ListOrganizationAuditRecords_ReturnsNewestFirst()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_order");
        var oldest = Record(AddMemberAction, FutureTime, scenario.OrgAdminId, scenario.OrganizationId);
        var middle = Record(CreatePolicyAction, FutureTime.AddMinutes(1), scenario.OrgAdminId, scenario.OrganizationId);
        var newest = Record(CreateClearanceAction, FutureTime.AddMinutes(2), scenario.OrgAdminId, scenario.OrganizationId);
        await SeedAsync(factory, middle, oldest, newest);

        var records = await ListAsync(scenario, string.Empty);

        Assert.Equal([newest.Id, middle.Id, oldest.Id], records.Take(3).Select(r => r.Id));
        Assert.Equal(records.OrderByDescending(r => r.OccurredAt).Select(r => r.Id), records.Select(r => r.Id));
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_MoreThanTheLimit_ReturnsTheNewest200()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_limit");
        var seeded = Enumerable.Range(0, MaxRecords + 1)
            .Select(i => Record(AddMemberAction, FutureTime.AddSeconds(i), scenario.OrgAdminId, scenario.OrganizationId))
            .ToArray();
        await SeedAsync(factory, seeded);

        var records = await ListAsync(scenario, string.Empty);

        Assert.Equal(MaxRecords, records.Count);
        Assert.Equal(seeded.Reverse().Take(MaxRecords).Select(r => r.Id), records.Select(r => r.Id));
    }

    // Records at the same time are ordered by id descending, so the cut at the limit is stable
    [Fact]
    public async Task ListOrganizationAuditRecords_MoreThanTheLimitAtTheSameTime_CutsTheLowestId()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_tie_limit");
        var seeded = TiedRecords(MaxRecords + 1, i => Record(AddMemberAction, FutureTime, scenario.OrgAdminId, scenario.OrganizationId));
        await SeedAsync(factory, seeded.Reverse().ToArray());

        var records = await ListAsync(scenario, string.Empty);

        Assert.Equal(seeded.Skip(1).Reverse().Select(r => r.Id), records.Select(r => r.Id));
    }

    // --- AC4 and AC5: the shape of an answer ---

    [Fact]
    public async Task ListOrganizationAuditRecords_OccurredAt_IsSerializedAsUtcWithZ()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_utc");
        var record = Record(AddMemberAction, FutureTime, scenario.OrgAdminId, scenario.OrganizationId);
        await SeedAsync(factory, record);

        var json = await ReadJsonAsync(await scenario.OrgAdmin.GetAsync(
            OrganizationAuditRecordsEndpoint(scenario.OrganizationId) + Filters(from: FutureTime, to: FutureTime)));

        var occurredAt = json.EnumerateArray().Single().GetProperty("occurredAt").GetString()!;
        Assert.EndsWith("Z", occurredAt);
        Assert.Equal(FutureTime, DateTime.Parse(occurredAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_Answer_HasOnlyTheSummaryFields()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_shape");
        await SeedAsync(factory, Record(AddMemberAction, FutureTime, scenario.OrgAdminId, scenario.OrganizationId, targetId: Guid.NewGuid()));

        var json = await ReadJsonAsync(await scenario.OrgAdmin.GetAsync(OrganizationAuditRecordsEndpoint(scenario.OrganizationId)));

        Assert.All(json.EnumerateArray(), item =>
            Assert.Equal(SummaryFields.ToHashSet(), item.EnumerateObject().Select(p => p.Name).ToHashSet()));
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_Answer_CarriesTheRecordsValues()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_values");
        var targetId = Guid.NewGuid();
        var record = Record(DownloadDataItemAction, FutureTime, scenario.OrgAdminId, scenario.OrganizationId,
            AuditOutcome.Denied, NotFoundErrorCode, targetId);
        await SeedAsync(factory, record);

        var summary = Assert.Single(await ListAsync(scenario, Filters(action: DownloadDataItemAction)));

        Assert.Equal(
            (record.Id, scenario.OrgAdminId, DownloadDataItemAction, DataItemTargetType, targetId, nameof(AuditOutcome.Denied), NotFoundErrorCode),
            (summary.Id, summary.ActorUserId!.Value, summary.Action, summary.TargetType!, summary.TargetId!.Value, summary.Outcome, summary.ErrorCode!));
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_KnownActor_ResolvesTheActorEmail()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_email");
        var (_, memberId) = await CreateUserAsync(factory, "audit_org_email_member");
        await SeedAsync(factory, Record(DownloadDataItemAction, FutureTime, memberId, scenario.OrganizationId));

        var summary = Assert.Single(await ListAsync(scenario, Filters(userId: memberId)));

        Assert.Equal($"audit_org_email_member{EmailDomain}", summary.ActorEmail);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_AnonymousActor_HasNoActorEmail()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_anonymous_actor");
        var record = Record(DownloadDataItemAction, FutureTime, null, scenario.OrganizationId);
        await SeedAsync(factory, record);

        var summary = Assert.Single(await ListAsync(scenario, Filters(from: FutureTime, to: FutureTime)));

        Assert.Equal((record.Id, (Guid?)null, (string?)null), (summary.Id, summary.ActorUserId, summary.ActorEmail));
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_ActorWhoseAccountNoLongerExists_HasNoActorEmail()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_missing_actor");
        var missingActorId = Guid.NewGuid();
        await SeedAsync(factory, Record(AddMemberAction, FutureTime, missingActorId, scenario.OrganizationId));

        var summary = Assert.Single(await ListAsync(scenario, Filters(userId: missingActorId)));

        Assert.Equal((missingActorId, (string?)null), (summary.ActorUserId!.Value, summary.ActorEmail));
    }

    // --- AC6: a query leaves no audit record ---

    [Fact]
    public async Task ListOrganizationAuditRecords_LeavesNoAuditRecord()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_org_not_audited");
        var before = (await StoredRecordsAsync(factory)).Count;

        (await scenario.OrgAdmin.GetAsync(OrganizationAuditRecordsEndpoint(scenario.OrganizationId))).EnsureSuccessStatusCode();
        await scenario.PlatformAdmin.GetAsync(OrganizationAuditRecordsEndpoint(scenario.OrganizationId));

        Assert.Equal(before, (await StoredRecordsAsync(factory)).Count);
    }

    public static TheoryData<string> NotOrgAdminKinds() => new(OrganizationCallers.NotOrgAdminKinds);

    private async Task<List<AuditRecordSummaryResponse>> ListAsync(OrganizationScenario scenario, string queryString) =>
        await ReadAsync(await scenario.OrgAdmin.GetAsync(OrganizationAuditRecordsEndpoint(scenario.OrganizationId) + queryString));

    // Five records a minute apart from FutureTime: before the range, at its start, inside, at its end, after it
    private async Task<(DbAuditRecord Before, DbAuditRecord AtFrom, DbAuditRecord Inside, DbAuditRecord AtTo, DbAuditRecord After)> SeedRangeAsync(Guid organizationId)
    {
        var records = Enumerable.Range(0, 5)
            .Select(i => Record(AddMemberAction, FutureTime.AddMinutes(i), Guid.NewGuid(), organizationId))
            .ToArray();
        await SeedAsync(factory, records);
        return (records[0], records[1], records[2], records[3], records[4]);
    }
}

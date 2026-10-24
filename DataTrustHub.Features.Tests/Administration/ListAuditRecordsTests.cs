using System.Net;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AuditTrailSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// #24 step 3 (ADR-0016): the platform admin reads the tenant-level audit trail, never a tenant's own trail.
// On the relational SQLite provider, so the action list, the filters and the left join are translated to SQL.
// The trail is shared by every test of the class, so most tests isolate their records with a new actor id
public class ListAuditRecordsTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    private const string EmailDomain = "@example.com";
    private const string AccountsEndpoint = "/admin/accounts";

    // Later than any other test's records, so the unfiltered test's records are always among the newest 200
    private static readonly DateTime LatestTime = FutureTime.AddYears(3);

    // --- AC2: only tenant-level actions; only the platform admin ---

    [Fact]
    public async Task ListAuditRecords_PlatformAdmin_ReturnsTheTenantLevelActionsOnly()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_scope");
        var actorId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var tenantLevel = TenantLevelActions.Select((a, i) => Record(a, FutureTime.AddMinutes(i), actorId)).ToArray();
        await SeedAsync(factory, tenantLevel);
        await SeedAsync(factory, InOrganizationRecords(actorId, organizationId));

        var records = await ListAsync(platformAdmin, Filters(userId: actorId));

        Assert.Equal(tenantLevel.Select(r => r.Id).ToHashSet(), records.Select(r => r.Id).ToHashSet());
    }

    [Fact]
    public async Task ListAuditRecords_Unfiltered_ReturnsNoActionInsideAnOrganization()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_unfiltered");
        var tenantLevel = Record(CreateOrganizationAction, LatestTime);
        await SeedAsync(factory, [tenantLevel, .. InOrganizationRecords(Guid.NewGuid(), Guid.NewGuid(), LatestTime)]);

        var records = await ListAsync(platformAdmin, string.Empty);

        Assert.Contains(records, r => r.Id == tenantLevel.Id);
        Assert.All(records, r => Assert.Contains(r.Action, TenantLevelActions));
    }

    [Theory]
    [InlineData(AddMemberAction)]
    [InlineData(CreatePolicyAction)]
    [InlineData(CreateClearanceAction)]
    [InlineData(DeleteDataItemAction)]
    [InlineData(DownloadDataItemAction)]
    public async Task ListAuditRecords_ActionFilterOnAnInOrganizationAction_ReturnsNothing(string action)
    {
        var platformAdmin = await CreatePlatformAdminAsync($"audit_platform_in_org_{action}");
        var actorId = Guid.NewGuid();
        await SeedAsync(factory, Record(action, FutureTime, actorId, Guid.NewGuid()), Record(action, FutureTime, actorId));

        var records = await ListAsync(platformAdmin, Filters(userId: actorId, action: action));

        Assert.Empty(records);
    }

    // The appointment of an organization admin names an organization, and is still a tenant-level action
    [Fact]
    public async Task ListAuditRecords_AppointmentInAnOrganization_IsInThePlatformTrail()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_platform_appoint");
        var platformAdminId = await PlatformAdminIdAsync(factory, "audit_platform_appoint");

        var records = await ListAsync(scenario.PlatformAdmin, Filters(userId: platformAdminId, action: AppointOrganizationAdminAction));

        Assert.Single(records);
    }

    [Fact]
    public async Task ListAuditRecords_RegularUser_Returns403()
    {
        var (user, _) = await CreateUserAsync(factory, "audit_platform_user");

        var response = await user.GetAsync(PlatformAuditRecordsEndpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListAuditRecords_OrganizationAdmin_Returns403()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_platform_orgadmin");

        var response = await scenario.OrgAdmin.GetAsync(PlatformAuditRecordsEndpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListAuditRecords_Anonymous_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(PlatformAuditRecordsEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- AC3: filters, each alone and combined with AND; from and to inclusive ---

    [Fact]
    public async Task ListAuditRecords_UserIdFilter_ReturnsOnlyThatActorsRecords()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_user_filter");
        var actorId = Guid.NewGuid();
        var mine = Record(CreateAccountAction, FutureTime, actorId);
        await SeedAsync(factory, mine, Record(CreateAccountAction, FutureTime, Guid.NewGuid()));

        var records = await ListAsync(platformAdmin, Filters(userId: actorId));

        Assert.Equal(mine.Id, Assert.Single(records).Id);
    }

    [Fact]
    public async Task ListAuditRecords_ActionFilter_ReturnsOnlyThatAction()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_action_filter");
        var actorId = Guid.NewGuid();
        var deleteAccount = Record(DeleteAccountAction, FutureTime, actorId);
        await SeedAsync(factory, deleteAccount, Record(CreateAccountAction, FutureTime, actorId));

        var records = await ListAsync(platformAdmin, Filters(action: DeleteAccountAction));

        Assert.Contains(records, r => r.Id == deleteAccount.Id);
        Assert.All(records, r => Assert.Equal(DeleteAccountAction, r.Action));
    }

    [Fact]
    public async Task ListAuditRecords_FromAndTo_AreInclusiveBounds()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_range");
        var (actorId, records) = await SeedRangeAsync();

        var answer = await ListAsync(platformAdmin, Filters(actorId, from: records[1].OccurredAt, to: records[3].OccurredAt));

        Assert.Equal(records[1..4].Select(r => r.Id).ToHashSet(), answer.Select(r => r.Id).ToHashSet());
    }

    [Fact]
    public async Task ListAuditRecords_FromAlone_ReturnsRecordsAtOrAfterIt()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_from");
        var (actorId, records) = await SeedRangeAsync();

        var answer = await ListAsync(platformAdmin, Filters(actorId, from: records[1].OccurredAt));

        Assert.Equal(records[1..].Select(r => r.Id).ToHashSet(), answer.Select(r => r.Id).ToHashSet());
    }

    [Fact]
    public async Task ListAuditRecords_ToAlone_ReturnsRecordsAtOrBeforeIt()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_to");
        var (actorId, records) = await SeedRangeAsync();

        var answer = await ListAsync(platformAdmin, Filters(actorId, to: records[3].OccurredAt));

        Assert.Equal(records[..4].Select(r => r.Id).ToHashSet(), answer.Select(r => r.Id).ToHashSet());
    }

    // A time without a zone is taken as UTC (the stored times are UTC)
    [Fact]
    public async Task ListAuditRecords_UnzonedTimes_AreTakenAsUtc()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_unzoned");
        var (actorId, records) = await SeedRangeAsync();
        var unzoned = $"?userId={actorId}&from={records[1].OccurredAt:yyyy-MM-ddTHH:mm:ss}&to={records[3].OccurredAt:yyyy-MM-ddTHH:mm:ss}";

        var answer = await ListAsync(platformAdmin, unzoned);

        Assert.Equal(records[1..4].Select(r => r.Id).ToHashSet(), answer.Select(r => r.Id).ToHashSet());
    }

    [Fact]
    public async Task ListAuditRecords_AllFiltersCombined_AreAnded()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_combined");
        var actorId = Guid.NewGuid();
        var match = Record(LoginUserAction, FutureTime.AddMinutes(1), actorId);
        await SeedAsync(factory,
            match,
            Record(RegisterUserAction, FutureTime.AddMinutes(1), actorId),
            Record(LoginUserAction, FutureTime.AddMinutes(1), Guid.NewGuid()),
            Record(LoginUserAction, FutureTime.AddHours(1), actorId));

        var records = await ListAsync(platformAdmin, Filters(actorId, LoginUserAction, FutureTime, FutureTime.AddMinutes(2)));

        Assert.Equal(match.Id, Assert.Single(records).Id);
    }

    [Fact]
    public async Task ListAuditRecords_ToBeforeFrom_Returns400()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_inverted");

        var response = await platformAdmin.GetAsync(PlatformAuditRecordsEndpoint + Filters(from: FutureTime, to: FutureTime.AddSeconds(-1)));

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.BadRequest, AdminErrorCodes.ValidationGeneral, AdminErrorMessages.ValidationGeneral);
    }

    [Fact]
    public async Task ListAuditRecords_ActionLongerThanTheColumn_Returns400()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_long_action");

        var response = await platformAdmin.GetAsync(PlatformAuditRecordsEndpoint + Filters(action: new string('a', ActionMaxLength + 1)));

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.BadRequest, AdminErrorCodes.ValidationGeneral, AdminErrorMessages.ValidationGeneral);
    }

    [Fact]
    public async Task ListAuditRecords_ActionOfTheColumnsLength_Returns200()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_max_action");

        var response = await platformAdmin.GetAsync(PlatformAuditRecordsEndpoint + Filters(action: new string('a', ActionMaxLength)));

        Assert.Empty(await ReadAsync(response));
    }

    [Fact]
    public async Task ListAuditRecords_FromEqualsTo_Returns200()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_equal_bounds");
        var actorId = Guid.NewGuid();
        var record = Record(CreateOrganizationAction, FutureTime, actorId);
        await SeedAsync(factory, record);

        var records = await ListAsync(platformAdmin, Filters(actorId, from: FutureTime, to: FutureTime));

        Assert.Equal(record.Id, Assert.Single(records).Id);
    }

    // --- order and limit ---

    [Fact]
    public async Task ListAuditRecords_ReturnsNewestFirst()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_order");
        var actorId = Guid.NewGuid();
        var oldest = Record(CreateOrganizationAction, FutureTime, actorId);
        var middle = Record(AppointOrganizationAdminAction, FutureTime.AddMinutes(1), actorId);
        var newest = Record(DeleteOrganizationAction, FutureTime.AddMinutes(2), actorId);
        await SeedAsync(factory, middle, newest, oldest);

        var records = await ListAsync(platformAdmin, Filters(userId: actorId));

        Assert.Equal([newest.Id, middle.Id, oldest.Id], records.Select(r => r.Id));
    }

    [Fact]
    public async Task ListAuditRecords_MoreThanTheLimit_ReturnsTheNewest200()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_limit");
        var actorId = Guid.NewGuid();
        var seeded = Enumerable.Range(0, MaxRecords + 1)
            .Select(i => Record(LoginUserAction, FutureTime.AddSeconds(i), actorId))
            .ToArray();
        await SeedAsync(factory, seeded);

        var records = await ListAsync(platformAdmin, Filters(userId: actorId));

        Assert.Equal(seeded.Reverse().Take(MaxRecords).Select(r => r.Id), records.Select(r => r.Id));
    }

    [Fact]
    public async Task ListAuditRecords_UnfilteredWithMoreThanTheLimit_Returns200NewestFirst()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_limit_unfiltered");
        await SeedAsync(factory, Enumerable.Range(0, MaxRecords + 1)
            .Select(i => Record(LoginUserAction, FutureTime.AddYears(1).AddSeconds(i))).ToArray());

        var records = await ListAsync(platformAdmin, string.Empty);

        Assert.Equal(MaxRecords, records.Count);
        Assert.Equal(records.OrderByDescending(r => r.OccurredAt).Select(r => r.Id), records.Select(r => r.Id));
    }

    // Records at the same time are ordered by id, newest-looking id first, so the cut at the limit is stable
    [Fact]
    public async Task ListAuditRecords_SameOccurredAt_AreOrderedByIdDescending()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_ties");
        var actorId = Guid.NewGuid();
        var seeded = TiedRecords(3, i => Record(LoginUserAction, FutureTime, actorId));
        await SeedAsync(factory, [seeded[1], seeded[0], seeded[2]]);

        var records = await ListAsync(platformAdmin, Filters(userId: actorId));

        Assert.Equal([seeded[2].Id, seeded[1].Id, seeded[0].Id], records.Select(r => r.Id));
    }

    [Fact]
    public async Task ListAuditRecords_MoreThanTheLimitAtTheSameTime_CutsTheLowestId()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_tie_limit");
        var actorId = Guid.NewGuid();
        var seeded = TiedRecords(MaxRecords + 1, i => Record(LoginUserAction, FutureTime, actorId));
        await SeedAsync(factory, seeded.Reverse().ToArray());

        var first = await ListAsync(platformAdmin, Filters(userId: actorId));
        var second = await ListAsync(platformAdmin, Filters(userId: actorId));

        Assert.Equal(seeded.Skip(1).Reverse().Select(r => r.Id), first.Select(r => r.Id));
        Assert.Equal(first.Select(r => r.Id), second.Select(r => r.Id));
    }

    // --- AC4 and AC5: the shape of an answer ---

    [Fact]
    public async Task ListAuditRecords_OccurredAt_IsSerializedAsUtcWithZ()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_utc");
        var actorId = Guid.NewGuid();
        await SeedAsync(factory, Record(CreateAccountAction, FutureTime, actorId));

        var json = await ReadJsonAsync(await platformAdmin.GetAsync(PlatformAuditRecordsEndpoint + Filters(userId: actorId)));

        var occurredAt = json.EnumerateArray().Single().GetProperty("occurredAt").GetString()!;
        Assert.EndsWith("Z", occurredAt);
        Assert.Equal(FutureTime, DateTime.Parse(occurredAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
    }

    // A record written by a real command (now, through the audit decorator) is returned in UTC too
    [Fact]
    public async Task ListAuditRecords_RecordOfARealCommand_IsReturnedInUtc()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_real_utc");
        var platformAdminId = await TestUsers.GetIdAsync(factory, $"audit_platform_real_utc{EmailDomain}");
        var before = DateTime.UtcNow.AddMinutes(-1);
        await AdminSetup.CreateOrganizationAsync(platformAdmin, "audit_platform_real_utc organization");

        var summary = Assert.Single(await ListAsync(platformAdmin, Filters(platformAdminId, CreateOrganizationAction)));

        Assert.Equal(DateTimeKind.Utc, summary.OccurredAt.Kind);
        Assert.InRange(summary.OccurredAt, before, DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task ListAuditRecords_Answer_HasOnlyTheSummaryFields()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_shape");

        var json = await ReadJsonAsync(await platformAdmin.GetAsync(PlatformAuditRecordsEndpoint));

        Assert.NotEmpty(json.EnumerateArray());
        Assert.All(json.EnumerateArray(), item =>
            Assert.Equal(SummaryFields.ToHashSet(), item.EnumerateObject().Select(p => p.Name).ToHashSet()));
    }

    [Fact]
    public async Task ListAuditRecords_Answer_CarriesTheRecordsValues()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_values");
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var record = Record(DeleteAccountAction, FutureTime, actorId, outcome: AuditOutcome.Rejected,
            errorCode: AdminErrorCodes.AccountNotFound, targetId: targetId);
        await SeedAsync(factory, record);

        var summary = Assert.Single(await ListAsync(platformAdmin, Filters(userId: actorId)));

        Assert.Equal(
            (record.Id, DeleteAccountAction, DataItemTargetType, targetId, nameof(AuditOutcome.Rejected), AdminErrorCodes.AccountNotFound),
            (summary.Id, summary.Action, summary.TargetType!, summary.TargetId!.Value, summary.Outcome, summary.ErrorCode!));
    }

    [Fact]
    public async Task ListAuditRecords_KnownActor_ResolvesTheActorEmail()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "audit_platform_email");
        var platformAdminId = await PlatformAdminIdAsync(factory, "audit_platform_email");

        var summary = Assert.Single(await ListAsync(scenario.PlatformAdmin, Filters(platformAdminId, CreateOrganizationAction)));

        Assert.Equal($"audit_platform_email_platform{EmailDomain}", summary.ActorEmail);
    }

    [Fact]
    public async Task ListAuditRecords_AnonymousActor_HasNoActorEmail()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_anonymous_actor");
        var occurredAt = FutureTime.AddYears(2);
        var record = Record(RegisterUserAction, occurredAt);
        await SeedAsync(factory, record);

        var summary = Assert.Single(await ListAsync(platformAdmin, Filters(from: occurredAt, to: occurredAt)));

        Assert.Equal((record.Id, (Guid?)null, (string?)null), (summary.Id, summary.ActorUserId, summary.ActorEmail));
    }

    [Fact]
    public async Task ListAuditRecords_ActorWhoseAccountWasDeleted_HasNoActorEmail()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_deleted_actor");
        var (_, userId) = await CreateUserAsync(factory, "audit_platform_deleted_actor_user");
        await SeedAsync(factory, Record(CreateAccountAction, FutureTime, userId));
        (await platformAdmin.DeleteAsync($"{AccountsEndpoint}/{userId}")).EnsureSuccessStatusCode();

        var summary = Assert.Single(await ListAsync(platformAdmin, Filters(userId, CreateAccountAction)));

        Assert.Equal((userId, (string?)null), (summary.ActorUserId!.Value, summary.ActorEmail));
    }

    [Fact]
    public async Task ListAuditRecords_ActorWhoNeverExisted_HasNoActorEmail()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_missing_actor");
        var missingActorId = Guid.NewGuid();
        await SeedAsync(factory, Record(LoginUserAction, FutureTime, missingActorId));

        var summary = Assert.Single(await ListAsync(platformAdmin, Filters(userId: missingActorId)));

        Assert.Null(summary.ActorEmail);
    }

    // --- AC6: a query leaves no audit record ---

    [Fact]
    public async Task ListAuditRecords_LeavesNoAuditRecord()
    {
        var platformAdmin = await CreatePlatformAdminAsync("audit_platform_not_audited");
        var before = (await StoredRecordsAsync(factory)).Count;

        (await platformAdmin.GetAsync(PlatformAuditRecordsEndpoint)).EnsureSuccessStatusCode();
        await platformAdmin.GetAsync(PlatformAuditRecordsEndpoint + Filters(from: FutureTime, to: FutureTime.AddSeconds(-1)));

        Assert.Equal(before, (await StoredRecordsAsync(factory)).Count);
    }

    private async Task<HttpClient> CreatePlatformAdminAsync(string name) =>
        await TestUsers.CreateClientAsync(factory, $"{name}{EmailDomain}", UserRoles.Admin);

    private static async Task<List<AuditRecordSummaryResponse>> ListAsync(HttpClient caller, string queryString) =>
        await ReadAsync(await caller.GetAsync(PlatformAuditRecordsEndpoint + queryString));

    // Records of actions inside an organization (members, policies, clearances, data items), with and without one
    private static DbAuditRecord[] InOrganizationRecords(Guid actorId, Guid organizationId, DateTime? occurredAt = null)
    {
        var time = occurredAt ?? FutureTime;
        return
        [
            Record(AddMemberAction, time, actorId, organizationId),
            Record(CreatePolicyAction, time, actorId, organizationId),
            Record(CreateClearanceAction, time, actorId, organizationId),
            Record(DeleteDataItemAction, time, actorId, organizationId),
            Record(DeleteDataItemAction, time, actorId),
            Record(DownloadDataItemAction, time, actorId, organizationId, AuditOutcome.Denied, NotFoundErrorCode)
        ];
    }

    // Five records of one new actor, a minute apart from FutureTime: before a range, at its start, inside, at its end, after it
    private async Task<(Guid ActorId, DbAuditRecord[] Records)> SeedRangeAsync()
    {
        var actorId = Guid.NewGuid();
        var records = Enumerable.Range(0, 5).Select(i => Record(LoginUserAction, FutureTime.AddMinutes(i), actorId)).ToArray();
        await SeedAsync(factory, records);
        return (actorId, records);
    }
}

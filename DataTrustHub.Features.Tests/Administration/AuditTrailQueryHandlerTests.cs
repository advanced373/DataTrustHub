using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Auditing;
using DataTrustHub.Features.Administration.ListAuditRecords;
using DataTrustHub.Features.Administration.ListOrganizationAuditRecords;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AuditTrailSetup;
using OrganizationSummary = DataTrustHub.Features.Administration.ListOrganizationAuditRecords.AuditRecordSummary;
using PlatformSummary = DataTrustHub.Features.Administration.ListAuditRecords.AuditRecordSummary;

namespace DataTrustHub.Features.Tests.Administration;

// #74 part 4e, AC3 and AC4 on the handlers: the two audit trail queries pass their scope (the tenant-level actions, or
// the organization), the filters with times as UTC, and the maximum number of records to the read side, and map its
// answer to their responses unchanged and in its order; the organization trail checks the organization admin before
// any read. Built directly with fakes, so no database is involved
public class AuditTrailQueryHandlerTests
{
    private const string ActorEmail = "audit_handler_actor@example.com";
    private const string TargetType = "DataItem";
    private const string FailedOutcome = "Failed";
    private const string SucceededOutcome = "Succeeded";

    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid OtherOrganizationId = Guid.NewGuid();
    private static readonly DateTime UtcFrom = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime UtcTo = new(2026, 11, 6, 18, 30, 0, DateTimeKind.Utc);

    private readonly FakeMembershipRepository _memberships = new();
    private readonly RecordingAuditRecordQueries _auditRecordQueries = new();

    public enum CallerKind
    {
        WithoutMembership,
        Member,
        Guest,
        OrgAdminOfAnotherOrganization
    }

    public static TheoryData<CallerKind> NonOrgAdminCallers() =>
        new(CallerKind.WithoutMembership, CallerKind.Member, CallerKind.Guest, CallerKind.OrgAdminOfAnotherOrganization);

    // --- ListAuditRecords: the platform admin's tenant-level trail ---

    [Fact]
    public async Task ListAuditRecords_AsksTheReadSideForTheTenantLevelActionsOnly()
    {
        await PlatformHandler().Handle(new ListAuditRecordsQuery(null, null, null, null), default);

        var call = Assert.Single(_auditRecordQueries.ActionCalls);
        Assert.Equal(TenantLevelActions.ToHashSet(), call.Actions.ToHashSet());
        Assert.Equal(TenantLevelActions.Length, call.Actions.Count);
        Assert.Empty(_auditRecordQueries.OrganizationCalls);
    }

    [Fact]
    public async Task ListAuditRecords_AsksTheReadSideForAtMostMaxRecords()
    {
        await PlatformHandler().Handle(new ListAuditRecordsQuery(null, null, null, null), default);

        Assert.Equal(MaxRecords, Assert.Single(_auditRecordQueries.ActionCalls).MaxRecords);
    }

    [Fact]
    public async Task ListAuditRecords_NoFilter_PassesAnEmptyFilter()
    {
        await PlatformHandler().Handle(new ListAuditRecordsQuery(null, null, null, null), default);

        Assert.Equal(new AuditRecordFilter(null, null, null, null), Assert.Single(_auditRecordQueries.ActionCalls).Filter);
    }

    [Fact]
    public async Task ListAuditRecords_EveryFilter_IsPassedToTheReadSide()
    {
        var userId = Guid.NewGuid();

        await PlatformHandler().Handle(new ListAuditRecordsQuery(userId, CreateAccountAction, UtcFrom, UtcTo), default);

        var filter = Assert.Single(_auditRecordQueries.ActionCalls).Filter;
        Assert.Equal(new AuditRecordFilter(userId, CreateAccountAction, UtcFrom, UtcTo), filter);
        Assert.Equal(DateTimeKind.Utc, filter.From!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, filter.To!.Value.Kind);
    }

    [Fact]
    public async Task ListAuditRecords_TimesWithoutAKind_AreTakenAsUtc()
    {
        var from = DateTime.SpecifyKind(UtcFrom, DateTimeKind.Unspecified);
        var to = DateTime.SpecifyKind(UtcTo, DateTimeKind.Unspecified);

        await PlatformHandler().Handle(new ListAuditRecordsQuery(null, null, from, to), default);

        AssertUtcBounds(Assert.Single(_auditRecordQueries.ActionCalls).Filter, UtcFrom, UtcTo);
    }

    [Fact]
    public async Task ListAuditRecords_LocalTimes_AreConvertedToUtc()
    {
        var from = UtcFrom.ToLocalTime();
        var to = UtcTo.ToLocalTime();

        await PlatformHandler().Handle(new ListAuditRecordsQuery(null, null, from, to), default);

        AssertUtcBounds(Assert.Single(_auditRecordQueries.ActionCalls).Filter, UtcFrom, UtcTo);
    }

    [Fact]
    public async Task ListAuditRecords_ReturnsTheReadSideRecordsInTheirOrderWithEveryField()
    {
        var (succeeded, failed) = TwoEntries();
        _auditRecordQueries.Entries = [succeeded, failed];

        var result = await PlatformHandler().Handle(new ListAuditRecordsQuery(null, null, null, null), default);

        Assert.True(result.IsSuccess);
        Assert.Equal([PlatformSummaryOf(succeeded), PlatformSummaryOf(failed)], result.Value);
        Assert.All(result.Value, summary => Assert.Equal(DateTimeKind.Utc, summary.OccurredAt.Kind));
    }

    [Fact]
    public async Task ListAuditRecords_ReadSideHasNoRecords_ReturnsEmpty()
    {
        var result = await PlatformHandler().Handle(new ListAuditRecordsQuery(null, null, null, null), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    // --- ListOrganizationAuditRecords: the organization admin check comes before any read ---

    [Theory]
    [MemberData(nameof(NonOrgAdminCallers))]
    public async Task ListOrganizationAuditRecords_CallerNotOrgAdmin_FailsWithOrganizationNotFoundWithoutReading(CallerKind kind)
    {
        _auditRecordQueries.Entries = [TwoEntries().Succeeded];

        var result = await OrganizationHandler().Handle(OrganizationQuery(Caller(kind)), default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            (AdminErrorCodes.OrganizationNotFound, ErrorType.NotFound),
            (result.Error.Code, result.Error.Type));
        Assert.Equal(0, _auditRecordQueries.Calls);
    }

    // Filters do not change the order: the check still comes first
    [Fact]
    public async Task ListOrganizationAuditRecords_CallerNotOrgAdminWithEveryFilter_DoesNotRead()
    {
        var query = new ListOrganizationAuditRecordsQuery(
            OrganizationId, Guid.NewGuid(), Guid.NewGuid(), AddMemberAction, UtcFrom, UtcTo);

        var result = await OrganizationHandler().Handle(query, default);

        Assert.True(result.IsFailure);
        Assert.Equal(0, _auditRecordQueries.Calls);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_OrgAdmin_AsksTheReadSideForThatOrganizationOnly()
    {
        await OrganizationHandler().Handle(OrganizationQuery(OrgAdmin()), default);

        Assert.Equal(OrganizationId, Assert.Single(_auditRecordQueries.OrganizationCalls).OrganizationId);
        Assert.Empty(_auditRecordQueries.ActionCalls);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_OrgAdmin_AsksTheReadSideForAtMostMaxRecords()
    {
        await OrganizationHandler().Handle(OrganizationQuery(OrgAdmin()), default);

        Assert.Equal(MaxRecords, Assert.Single(_auditRecordQueries.OrganizationCalls).MaxRecords);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_OrgAdmin_EveryFilterIsPassedToTheReadSide()
    {
        var userId = Guid.NewGuid();
        var query = new ListOrganizationAuditRecordsQuery(OrganizationId, OrgAdmin(), userId, AddMemberAction, UtcFrom, UtcTo);

        await OrganizationHandler().Handle(query, default);

        Assert.Equal(
            new AuditRecordFilter(userId, AddMemberAction, UtcFrom, UtcTo),
            Assert.Single(_auditRecordQueries.OrganizationCalls).Filter);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_OrgAdmin_NoFilter_PassesAnEmptyFilter()
    {
        await OrganizationHandler().Handle(OrganizationQuery(OrgAdmin()), default);

        Assert.Equal(
            new AuditRecordFilter(null, null, null, null),
            Assert.Single(_auditRecordQueries.OrganizationCalls).Filter);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_OrgAdmin_TimesWithoutAKindAreTakenAsUtc()
    {
        var query = new ListOrganizationAuditRecordsQuery(
            OrganizationId, OrgAdmin(), null, null,
            DateTime.SpecifyKind(UtcFrom, DateTimeKind.Unspecified), DateTime.SpecifyKind(UtcTo, DateTimeKind.Unspecified));

        await OrganizationHandler().Handle(query, default);

        AssertUtcBounds(Assert.Single(_auditRecordQueries.OrganizationCalls).Filter, UtcFrom, UtcTo);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_OrgAdmin_LocalTimesAreConvertedToUtc()
    {
        var query = new ListOrganizationAuditRecordsQuery(
            OrganizationId, OrgAdmin(), null, null, UtcFrom.ToLocalTime(), UtcTo.ToLocalTime());

        await OrganizationHandler().Handle(query, default);

        AssertUtcBounds(Assert.Single(_auditRecordQueries.OrganizationCalls).Filter, UtcFrom, UtcTo);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_OrgAdmin_ReturnsTheReadSideRecordsInTheirOrderWithEveryField()
    {
        var (succeeded, failed) = TwoEntries();
        _auditRecordQueries.Entries = [failed, succeeded];

        var result = await OrganizationHandler().Handle(OrganizationQuery(OrgAdmin()), default);

        Assert.True(result.IsSuccess);
        Assert.Equal([OrganizationSummaryOf(failed), OrganizationSummaryOf(succeeded)], result.Value);
    }

    [Fact]
    public async Task ListOrganizationAuditRecords_OrgAdmin_ReadSideHasNoRecords_ReturnsEmpty()
    {
        var result = await OrganizationHandler().Handle(OrganizationQuery(OrgAdmin()), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    // --- helpers ---

    private ListAuditRecordsHandler PlatformHandler() => new(_auditRecordQueries);

    private ListOrganizationAuditRecordsHandler OrganizationHandler() => new(_memberships, _auditRecordQueries);

    private static ListOrganizationAuditRecordsQuery OrganizationQuery(Guid callerId) =>
        new(OrganizationId, callerId, null, null, null, null);

    private Guid OrgAdmin() => _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), OrganizationId)).UserId;

    private Guid Caller(CallerKind kind) => kind switch
    {
        CallerKind.WithoutMembership => Guid.NewGuid(),
        CallerKind.Member => _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OrganizationId)).UserId,
        CallerKind.Guest => _memberships.Seed(Membership.AddGuest(Guid.NewGuid(), OrganizationId)).UserId,
        CallerKind.OrgAdminOfAnotherOrganization =>
            _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), OtherOrganizationId)).UserId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    // Same instants as the expected UTC bounds, and of kind UTC
    private static void AssertUtcBounds(AuditRecordFilter filter, DateTime from, DateTime to)
    {
        Assert.Equal((from, DateTimeKind.Utc), (filter.From!.Value, filter.From.Value.Kind));
        Assert.Equal((to, DateTimeKind.Utc), (filter.To!.Value, filter.To.Value.Kind));
    }

    // A succeeded record by a known actor with a target, and a failed anonymous one without a target or email
    private static (AuditRecordEntry Succeeded, AuditRecordEntry Failed) TwoEntries() =>
    (
        new AuditRecordEntry(
            Guid.NewGuid(), UtcTo, Guid.NewGuid(), ActorEmail, CreateAccountAction, TargetType, Guid.NewGuid(),
            SucceededOutcome, null),
        new AuditRecordEntry(
            Guid.NewGuid(), UtcFrom, null, null, LoginUserAction, null, null, FailedOutcome, NotFoundErrorCode)
    );

    private static PlatformSummary PlatformSummaryOf(AuditRecordEntry entry) =>
        new(entry.Id, entry.OccurredAt, entry.ActorUserId, entry.ActorEmail, entry.Action, entry.TargetType,
            entry.TargetId, entry.Outcome, entry.ErrorCode);

    private static OrganizationSummary OrganizationSummaryOf(AuditRecordEntry entry) =>
        new(entry.Id, entry.OccurredAt, entry.ActorUserId, entry.ActorEmail, entry.Action, entry.TargetType,
            entry.TargetId, entry.Outcome, entry.ErrorCode);

    // Records every read, so a test can prove what the handler asked for and that it did not read before its check
    private sealed class RecordingAuditRecordQueries : IAuditRecordQueries
    {
        internal IReadOnlyList<AuditRecordEntry> Entries { get; set; } = [];
        internal List<(IReadOnlyCollection<string> Actions, AuditRecordFilter Filter, int MaxRecords)> ActionCalls { get; } = [];
        internal List<(Guid OrganizationId, AuditRecordFilter Filter, int MaxRecords)> OrganizationCalls { get; } = [];
        internal int Calls => ActionCalls.Count + OrganizationCalls.Count;

        public Task<IReadOnlyList<AuditRecordEntry>> ListForActionsAsync(
            IReadOnlyCollection<string> actions,
            AuditRecordFilter filter,
            int maxRecords,
            CancellationToken cancellationToken)
        {
            ActionCalls.Add((actions, filter, maxRecords));
            return Task.FromResult(Entries);
        }

        public Task<IReadOnlyList<AuditRecordEntry>> ListForOrganizationAsync(
            Guid organizationId,
            AuditRecordFilter filter,
            int maxRecords,
            CancellationToken cancellationToken)
        {
            OrganizationCalls.Add((organizationId, filter, maxRecords));
            return Task.FromResult(Entries);
        }
    }
}

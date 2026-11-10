using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Clearances;
using DataTrustHub.Domain.ReadModels.Policies;
using DataTrustHub.Features.Administration.GetClearance;
using DataTrustHub.Features.Administration.GetPolicy;
using DataTrustHub.Features.Administration.ListClearances;
using DataTrustHub.Features.Administration.ListPolicies;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// #74 part 4c, AC4: the policy and clearance queries keep their authorization on the membership repository, checked
// before the read side is asked for anything, and pass the organization of the route (and the filters) to the read
// side, which does the scoping. Built directly with fakes, so no database is involved
public class PolicyAndClearanceQueryHandlerTests
{
    private const string PolicyName = "handler policy";
    private const string ClearanceName = "handler clearance";
    private const string LevelName = "Secret";
    private const int LevelPriority = 2;

    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid OtherOrganizationId = Guid.NewGuid();

    private readonly FakeMembershipRepository _memberships = new();
    private readonly RecordingPolicyQueries _policyQueries = new();
    private readonly RecordingClearanceQueries _clearanceQueries = new();

    public enum CallerKind
    {
        WithoutMembership,
        Member,
        Guest,
        OrgAdminOfAnotherOrganization
    }

    public static TheoryData<CallerKind> NonOrgAdminCallers() =>
        new(CallerKind.WithoutMembership, CallerKind.Member, CallerKind.Guest, CallerKind.OrgAdminOfAnotherOrganization);

    // --- the organization admin check comes before any read ---

    [Theory]
    [MemberData(nameof(NonOrgAdminCallers))]
    public async Task GetPolicy_CallerNotOrgAdmin_FailsWithOrganizationNotFoundWithoutReading(CallerKind kind)
    {
        var result = await GetPolicyHandler().Handle(new GetPolicyQuery(OrganizationId, Caller(kind), Guid.NewGuid()), default);

        AssertError(result, AdminErrorCodes.OrganizationNotFound);
        Assert.Equal(0, _policyQueries.Calls);
    }

    [Theory]
    [MemberData(nameof(NonOrgAdminCallers))]
    public async Task ListPolicies_CallerNotOrgAdmin_FailsWithOrganizationNotFoundWithoutReading(CallerKind kind)
    {
        var result = await ListPoliciesHandler().Handle(new ListPoliciesQuery(OrganizationId, Caller(kind)), default);

        AssertError(result, AdminErrorCodes.OrganizationNotFound);
        Assert.Equal(0, _policyQueries.Calls);
    }

    [Theory]
    [MemberData(nameof(NonOrgAdminCallers))]
    public async Task GetClearance_CallerNotOrgAdmin_FailsWithOrganizationNotFoundWithoutReading(CallerKind kind)
    {
        var result = await GetClearanceHandler().Handle(
            new GetClearanceQuery(OrganizationId, Caller(kind), Guid.NewGuid()), default);

        AssertError(result, AdminErrorCodes.OrganizationNotFound);
        Assert.Equal(0, _clearanceQueries.Calls);
    }

    [Theory]
    [MemberData(nameof(NonOrgAdminCallers))]
    public async Task ListClearances_CallerNotOrgAdmin_FailsWithOrganizationNotFoundWithoutReading(CallerKind kind)
    {
        var result = await ListClearancesHandler().Handle(
            new ListClearancesQuery(OrganizationId, Caller(kind), Guid.NewGuid(), Guid.NewGuid()), default);

        AssertError(result, AdminErrorCodes.OrganizationNotFound);
        Assert.Equal(0, _clearanceQueries.Calls);
    }

    // --- policies: the org admin reads through the port, scoped to the route organization ---

    [Fact]
    public async Task GetPolicy_OrgAdmin_ReturnsTheReadSidePolicyAndAsksForTheRouteOrganization()
    {
        var policy = new PolicyEntry(Guid.NewGuid(), PolicyName, OrganizationId);
        _policyQueries.Found = policy;

        var result = await GetPolicyHandler().Handle(new GetPolicyQuery(OrganizationId, OrgAdmin(), policy.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new GetPolicyResponse(policy.Id, policy.Name, policy.OrganizationId), result.Value);
        Assert.Equal((OrganizationId, policy.Id), Assert.Single(_policyQueries.Finds));
    }

    [Fact]
    public async Task GetPolicy_OrgAdmin_ReadSideFindsNothing_FailsWithPolicyNotFound()
    {
        var result = await GetPolicyHandler().Handle(new GetPolicyQuery(OrganizationId, OrgAdmin(), Guid.NewGuid()), default);

        AssertError(result, AdminErrorCodes.PolicyNotFound);
    }

    [Fact]
    public async Task ListPolicies_OrgAdmin_ReturnsTheReadSidePoliciesInTheirOrder()
    {
        var second = new PolicyEntry(Guid.NewGuid(), "b " + PolicyName, OrganizationId);
        var first = new PolicyEntry(Guid.NewGuid(), "a " + PolicyName, OrganizationId);
        _policyQueries.Listed = [first, second];

        var result = await ListPoliciesHandler().Handle(new ListPoliciesQuery(OrganizationId, OrgAdmin()), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [new PolicySummary(first.Id, first.Name, first.OrganizationId), new PolicySummary(second.Id, second.Name, second.OrganizationId)],
            result.Value);
        Assert.Equal(OrganizationId, Assert.Single(_policyQueries.Lists));
    }

    // --- clearances: the org admin reads through the port, scoped to the route organization ---

    [Fact]
    public async Task GetClearance_OrgAdmin_ReturnsTheReadSideClearanceAndAsksForTheRouteOrganization()
    {
        var clearance = NewClearance(ClearanceName);
        _clearanceQueries.Found = clearance;

        var result = await GetClearanceHandler().Handle(
            new GetClearanceQuery(OrganizationId, OrgAdmin(), clearance.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new GetClearanceResponse(clearance.Id, clearance.Name, clearance.UserId, clearance.PolicyId, LevelName, LevelPriority),
            result.Value);
        Assert.Equal((OrganizationId, clearance.Id), Assert.Single(_clearanceQueries.Finds));
    }

    [Fact]
    public async Task GetClearance_OrgAdmin_ReadSideFindsNothing_FailsWithClearanceNotFound()
    {
        var result = await GetClearanceHandler().Handle(
            new GetClearanceQuery(OrganizationId, OrgAdmin(), Guid.NewGuid()), default);

        AssertError(result, AdminErrorCodes.ClearanceNotFound);
    }

    [Fact]
    public async Task ListClearances_OrgAdmin_ReturnsTheReadSideClearancesInTheirOrder()
    {
        var first = NewClearance("a " + ClearanceName);
        var second = NewClearance("b " + ClearanceName);
        _clearanceQueries.Listed = [first, second];

        var result = await ListClearancesHandler().Handle(
            new ListClearancesQuery(OrganizationId, OrgAdmin(), null, null), default);

        Assert.True(result.IsSuccess);
        Assert.Equal([ToSummary(first), ToSummary(second)], result.Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ListClearances_OrgAdmin_PassesTheRouteOrganizationAndTheFiltersToTheReadSide(bool byUser, bool byPolicy)
    {
        Guid? userId = byUser ? Guid.NewGuid() : null;
        Guid? policyId = byPolicy ? Guid.NewGuid() : null;

        await ListClearancesHandler().Handle(new ListClearancesQuery(OrganizationId, OrgAdmin(), userId, policyId), default);

        Assert.Equal((OrganizationId, userId, policyId), Assert.Single(_clearanceQueries.Lists));
    }

    // --- helpers ---

    private GetPolicyHandler GetPolicyHandler() => new(_memberships, _policyQueries);

    private ListPoliciesHandler ListPoliciesHandler() => new(_memberships, _policyQueries);

    private GetClearanceHandler GetClearanceHandler() => new(_memberships, _clearanceQueries);

    private ListClearancesHandler ListClearancesHandler() => new(_memberships, _clearanceQueries);

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

    private static ClearanceEntry NewClearance(string name) =>
        new(Guid.NewGuid(), name, Guid.NewGuid(), Guid.NewGuid(), LevelName, LevelPriority);

    private static ClearanceSummary ToSummary(ClearanceEntry clearance) =>
        new(clearance.Id, clearance.Name, clearance.UserId, clearance.PolicyId, LevelName, LevelPriority);

    private static void AssertError<T>(Result<T> result, string code)
    {
        Assert.True(result.IsFailure);
        Assert.Equal((code, ErrorType.NotFound), (result.Error.Code, result.Error.Type));
    }

    // Records every read, so a test can prove the handler did not read before its authorization check
    private sealed class RecordingPolicyQueries : IPolicyQueries
    {
        internal IReadOnlyList<PolicyEntry> Listed { get; set; } = [];
        internal PolicyEntry? Found { get; set; }
        internal List<Guid> Lists { get; } = [];
        internal List<(Guid OrganizationId, Guid PolicyId)> Finds { get; } = [];
        internal int Calls => Lists.Count + Finds.Count;

        public Task<IReadOnlyList<PolicyEntry>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
        {
            Lists.Add(organizationId);
            return Task.FromResult(Listed);
        }

        public Task<PolicyEntry?> FindAsync(Guid organizationId, Guid policyId, CancellationToken cancellationToken)
        {
            Finds.Add((organizationId, policyId));
            return Task.FromResult(Found);
        }
    }

    private sealed class RecordingClearanceQueries : IClearanceQueries
    {
        internal IReadOnlyList<ClearanceEntry> Listed { get; set; } = [];
        internal ClearanceEntry? Found { get; set; }
        internal List<(Guid OrganizationId, Guid? UserId, Guid? PolicyId)> Lists { get; } = [];
        internal List<(Guid OrganizationId, Guid ClearanceId)> Finds { get; } = [];
        internal int Calls => Lists.Count + Finds.Count;

        public Task<IReadOnlyList<ClearanceEntry>> ListAsync(
            Guid organizationId, Guid? userId, Guid? policyId, CancellationToken cancellationToken)
        {
            Lists.Add((organizationId, userId, policyId));
            return Task.FromResult(Listed);
        }

        public Task<ClearanceEntry?> FindAsync(Guid organizationId, Guid clearanceId, CancellationToken cancellationToken)
        {
            Finds.Add((organizationId, clearanceId));
            return Task.FromResult(Found);
        }
    }
}

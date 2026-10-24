using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features.Administration.CreateClearance;
using DataTrustHub.Features.Administration.DeleteClearance;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// AC2 and AC4 of #95 step 3b on the handlers, built with fakes: the refusal order of granting a clearance
// (C6 caller check, policy in the organization, user, C5, C8, C3) and the organization scope of revoking one.
// Every refusal stages nothing and does not commit.
public class OrganizationClearanceCommandHandlerTests
{
    private const string ClearanceName = "Organization clearance";
    private const string LevelName = "Secret";
    private const int LevelPriority = 2;
    private const string PolicyName = "Organization policy";
    private const string UserEmail = "org.clearance.user@example.com";
    private const string CallerEmail = "org.clearance.orgadmin@example.com";

    private readonly FakeMembershipRepository _memberships = new();
    private readonly FakeUserRepository _users = new();
    private readonly FakePolicyRepository _policies = new();
    private readonly FakeClearanceRepository _clearances = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly Guid _otherOrganizationId = Guid.NewGuid();
    private readonly User _caller;

    public OrganizationClearanceCommandHandlerTests()
    {
        _caller = _users.Seed(CallerEmail);
        _memberships.Seed(Membership.AppointOrgAdmin(_caller.Id, _organizationId));
    }

    // --- grant: C6, the caller check comes first ---

    // Everything else is wrong too, so only the caller check can answer
    [Fact]
    public async Task Grant_CallerNotOrgAdminAndEverythingElseWrong_ReportsOrganizationNotFound()
    {
        var notOrgAdmin = _users.Seed(UserEmail);
        _memberships.Seed(Membership.AddMember(notOrgAdmin.Id, _organizationId));

        var result = await CreateHandler().Handle(
            Command(Guid.NewGuid(), Guid.NewGuid()) with { CallerId = notOrgAdmin.Id }, default);

        AssertNotGranted(result, AdminErrorCodes.OrganizationNotFound);
    }

    // An organization admin of another organization is not an organization admin here
    [Fact]
    public async Task Grant_CallerOrgAdminOfAnotherOrganization_ReportsOrganizationNotFound()
    {
        var user = SeedUserWith(Membership.AddMember);
        var policy = _policies.Seed(PolicyName, _organizationId);

        var result = await CreateHandler().Handle(Command(user.Id, policy.Id) with { OrganizationId = _otherOrganizationId }, default);

        AssertNotGranted(result, AdminErrorCodes.OrganizationNotFound);
    }

    // --- grant: the refusals in order ---

    // A policy of another organization is not found, even for a user who is also a member there
    [Fact]
    public async Task Grant_PolicyOfAnotherOrganization_ReportsPolicyNotFound()
    {
        var user = SeedUserWith(Membership.AddMember);
        _memberships.Seed(Membership.AddMember(user.Id, _otherOrganizationId));
        var foreignPolicy = _policies.Seed(PolicyName, _otherOrganizationId);

        var result = await CreateHandler().Handle(Command(user.Id, foreignPolicy.Id), default);

        AssertNotGranted(result, AdminErrorCodes.ClearancePolicyNotFound);
    }

    // The unknown user has no membership either: the user check comes before C5
    [Fact]
    public async Task Grant_UnknownUser_ReportsUserNotFoundBeforeNotMember()
    {
        var policy = _policies.Seed(PolicyName, _organizationId);

        var result = await CreateHandler().Handle(Command(Guid.NewGuid(), policy.Id), default);

        AssertNotGranted(result, AdminErrorCodes.ClearanceUserNotFound);
    }

    // Invariant C5
    [Fact]
    public async Task Grant_UserWithoutMembership_ReportsUserNotMember()
    {
        var user = _users.Seed(UserEmail);
        var policy = _policies.Seed(PolicyName, _organizationId);

        var result = await CreateHandler().Handle(Command(user.Id, policy.Id), default);

        AssertNotGranted(result, AdminErrorCodes.ClearanceUserNotMember);
    }

    // Invariant C5: a membership in another organization does not count
    [Fact]
    public async Task Grant_UserMemberOfAnotherOrganizationOnly_ReportsUserNotMember()
    {
        var user = _users.Seed(UserEmail);
        _memberships.Seed(Membership.AddMember(user.Id, _otherOrganizationId));
        var policy = _policies.Seed(PolicyName, _organizationId);

        var result = await CreateHandler().Handle(Command(user.Id, policy.Id), default);

        AssertNotGranted(result, AdminErrorCodes.ClearanceUserNotMember);
    }

    // A clearance already links the ids, but the user is not a member: C5 comes before C3
    [Fact]
    public async Task Grant_NonMemberAlreadyGranted_ReportsUserNotMemberBeforeAlreadyGranted()
    {
        var user = _users.Seed(UserEmail);
        var policy = _policies.Seed(PolicyName, _organizationId);
        _clearances.Seed(user.Id, policy.Id);

        var result = await CreateHandler().Handle(Command(user.Id, policy.Id), default);

        AssertNotGranted(result, AdminErrorCodes.ClearanceUserNotMember);
    }

    // Invariant C8: the caller is a member (as organization admin), so only C8 refuses
    [Fact]
    public async Task Grant_OrgAdminToThemselves_ReportsCannotGrantSelf()
    {
        var policy = _policies.Seed(PolicyName, _organizationId);

        var result = await CreateHandler().Handle(Command(_caller.Id, policy.Id), default);

        AssertNotGranted(result, AdminErrorCodes.ClearanceCannotGrantSelf);
    }

    // The caller already holds the clearance: C8 comes before C3
    [Fact]
    public async Task Grant_OrgAdminToThemselvesAlreadyGranted_ReportsCannotGrantSelfBeforeAlreadyGranted()
    {
        var policy = _policies.Seed(PolicyName, _organizationId);
        _clearances.Seed(_caller.Id, policy.Id);

        var result = await CreateHandler().Handle(Command(_caller.Id, policy.Id), default);

        AssertNotGranted(result, AdminErrorCodes.ClearanceCannotGrantSelf);
    }

    // Invariant C3
    [Fact]
    public async Task Grant_MemberAlreadyGranted_ReportsAlreadyGranted()
    {
        var user = SeedUserWith(Membership.AddMember);
        var policy = _policies.Seed(PolicyName, _organizationId);
        _clearances.Seed(user.Id, policy.Id);

        var result = await CreateHandler().Handle(Command(user.Id, policy.Id), default);

        AssertNotGranted(result, AdminErrorCodes.ClearanceAlreadyGranted);
    }

    // --- grant: success for each kind of membership (C5) ---

    [Fact]
    public async Task Grant_Member_AddsClearanceAndCommitsOnce() =>
        await AssertGrantedAsync(SeedUserWith(Membership.AddMember));

    [Fact]
    public async Task Grant_Guest_AddsClearanceAndCommitsOnce() =>
        await AssertGrantedAsync(SeedUserWith(Membership.AddGuest));

    // An organization admin may hold a clearance, granted by another organization admin (note under AC2)
    [Fact]
    public async Task Grant_AnotherOrgAdmin_AddsClearanceAndCommitsOnce() =>
        await AssertGrantedAsync(SeedUserWith(Membership.AppointOrgAdmin));

    // --- revoke (AC4) ---

    [Fact]
    public async Task Revoke_ClearanceOnPolicyOfAnotherOrganization_ReportsClearanceNotFound()
    {
        var foreignPolicy = _policies.Seed(PolicyName, _otherOrganizationId);
        var foreignClearance = _clearances.Seed(Guid.NewGuid(), foreignPolicy.Id);

        var result = await DeleteHandler().Handle(DeleteCommand(foreignClearance.Id), default);

        AssertNotRevoked(result, AdminErrorCodes.ClearanceNotFound);
    }

    // The clearance exists here, so only the caller check can answer
    [Fact]
    public async Task Revoke_CallerNotOrgAdmin_ReportsOrganizationNotFound()
    {
        var notOrgAdmin = SeedUserWith(Membership.AddMember);
        var policy = _policies.Seed(PolicyName, _organizationId);
        var clearance = _clearances.Seed(notOrgAdmin.Id, policy.Id);

        var result = await DeleteHandler().Handle(DeleteCommand(clearance.Id) with { CallerId = notOrgAdmin.Id }, default);

        AssertNotRevoked(result, AdminErrorCodes.OrganizationNotFound);
    }

    // Revoking one's own clearance only removes access (ADR-0014)
    [Fact]
    public async Task Revoke_OwnClearance_RemovesItAndCommitsOnce()
    {
        var policy = _policies.Seed(PolicyName, _organizationId);
        var ownClearance = _clearances.Seed(_caller.Id, policy.Id);

        var result = await DeleteHandler().Handle(DeleteCommand(ownClearance.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ownClearance.Id, Assert.Single(_clearances.Removed).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    // --- helpers ---

    private User SeedUserWith(Func<Guid, Guid, Membership> createMembership)
    {
        var user = _users.Seed(UserEmail);
        _memberships.Seed(createMembership(user.Id, _organizationId));
        return user;
    }

    private async Task AssertGrantedAsync(User user)
    {
        var policy = _policies.Seed(PolicyName, _organizationId);

        var result = await CreateHandler().Handle(Command(user.Id, policy.Id), default);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(_clearances.Added);
        Assert.Equal((result.Value, user.Id, policy.Id), (added.Id, added.UserId, added.PolicyId));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    private void AssertNotGranted(Result<Guid> result, string expectedCode)
    {
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Empty(_clearances.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    private void AssertNotRevoked(Result<Guid> result, string expectedCode)
    {
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Empty(_clearances.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    private CreateClearanceCommand Command(Guid userId, Guid policyId) =>
        new(_organizationId, _caller.Id, userId, policyId, ClearanceName, LevelName, LevelPriority);

    private DeleteClearanceCommand DeleteCommand(Guid clearanceId) => new(_organizationId, _caller.Id, clearanceId);

    private CreateClearanceHandler CreateHandler() => new(_memberships, _users, _policies, _clearances, _unitOfWork);

    private DeleteClearanceHandler DeleteHandler() => new(_memberships, _policies, _clearances, _unitOfWork);
}

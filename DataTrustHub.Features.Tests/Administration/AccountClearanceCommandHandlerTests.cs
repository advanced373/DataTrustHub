using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features.Administration.CreateAccount;
using DataTrustHub.Features.Administration.CreateClearance;
using DataTrustHub.Features.Administration.DeleteAccount;
using DataTrustHub.Features.Administration.DeleteClearance;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// ADR-0012 step 3b-2: the account and clearance commands run on repositories and commit exactly once,
// only on success. The handlers are built directly with fakes, so no database is involved.
public class AccountClearanceCommandHandlerTests
{
    private const string Email = "Handler.User@Example.com";
    private const string Password = "Password1!";
    private const string ClearanceName = "Handler clearance";
    private const string LevelName = "Secret";
    private const int LevelPriority = 2;

    private readonly FakeUserRepository _users = new();
    private readonly FakePolicyRepository _policies = new();
    private readonly FakeClearanceRepository _clearances = new();
    private readonly FakeDataItemRepository _dataItems = new();
    private readonly FakeMembershipRepository _memberships = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    // The clearance commands run for an organization admin of the organization (#95 step 3b, C6)
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();

    // --- CreateAccount ---

    [Fact]
    public async Task CreateAccount_NewEmail_AddsRegularUserWithHashedPasswordAndCommitsOnce()
    {
        var result = await CreateAccountHandler().Handle(new CreateAccountCommand(Email, Password), default);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(_users.Added);
        Assert.Equal((result.Value, Email, UserRoles.User), (added.Id, added.Email, added.Role));
        Assert.NotEqual(Password, added.HashedPassword);
        Assert.True(_hasher.VerifyHashedPassword(added.HashedPassword, Password));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreateAccount_EmailInUse_FailsWithoutCommit()
    {
        _users.Seed(Email);

        var result = await CreateAccountHandler().Handle(new CreateAccountCommand(Email, Password), default);

        Assert.Equal(AdminErrorCodes.AccountEmailAlreadyInUse, result.Error.Code);
        Assert.Empty(_users.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // --- DeleteAccount ---

    [Fact]
    public async Task DeleteAccount_Existing_StagesWholeCascadeInOneCommit()
    {
        var user = _users.Seed(Email);
        var clearance = _clearances.Seed(user.Id, Guid.NewGuid());
        var otherClearance = _clearances.Seed(Guid.NewGuid(), Guid.NewGuid());
        var activeItem = _dataItems.Seed(user.Id);
        _dataItems.Seed(user.Id, isDeleted: true);
        _dataItems.Seed(Guid.NewGuid());

        var result = await DeleteAccountHandler().Handle(new DeleteAccountCommand(user.Id, Guid.NewGuid()), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, Assert.Single(_users.Removed).Id);
        Assert.Equal(clearance.Id, Assert.Single(_clearances.Removed).Id);
        Assert.DoesNotContain(otherClearance, _clearances.Removed);
        var updated = Assert.Single(_dataItems.Updated);
        Assert.Equal(activeItem.Id, updated.Id);
        Assert.True(updated.IsDeleted);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    // AC5 of #95 step 2b: every membership of the user ends in the same save; other users' memberships stay
    [Fact]
    public async Task DeleteAccount_UserWithMemberships_RemovesEveryMembershipInTheSameCommit()
    {
        var user = _users.Seed(Email);
        var orgAdminMembership = _memberships.Seed(Membership.AppointOrgAdmin(user.Id, Guid.NewGuid()));
        var memberMembership = _memberships.Seed(Membership.AddMember(user.Id, Guid.NewGuid()));
        var guestMembership = _memberships.Seed(Membership.AddGuest(user.Id, Guid.NewGuid()));
        var otherUsersMembership = _memberships.Seed(Membership.AddMember(Guid.NewGuid(), memberMembership.OrganizationId));

        var result = await DeleteAccountHandler().Handle(new DeleteAccountCommand(user.Id, Guid.NewGuid()), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new HashSet<Guid> { orgAdminMembership.Id, memberMembership.Id, guestMembership.Id },
            _memberships.Removed.Select(m => m.Id).ToHashSet());
        Assert.DoesNotContain(otherUsersMembership, _memberships.Removed);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    // M2 does not block deleting an account: the organization's last organization admin goes too
    [Fact]
    public async Task DeleteAccount_LastOrgAdminOfOrganization_StillDeletesAndRemovesTheMembership()
    {
        var user = _users.Seed(Email);
        var lastOrgAdmin = _memberships.Seed(Membership.AppointOrgAdmin(user.Id, Guid.NewGuid()));

        var result = await DeleteAccountHandler().Handle(new DeleteAccountCommand(user.Id, Guid.NewGuid()), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(lastOrgAdmin.Id, Assert.Single(_memberships.Removed).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DeleteAccount_Missing_FailsWithoutStagingOrCommit()
    {
        var result = await DeleteAccountHandler().Handle(new DeleteAccountCommand(Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.Equal(AdminErrorCodes.AccountNotFound, result.Error.Code);
        AssertNothingStagedForAccountDelete();
    }

    [Fact]
    public async Task DeleteAccount_OwnAccount_FailsWithoutStagingOrCommit()
    {
        var admin = _users.Seed(Email);
        _clearances.Seed(admin.Id, Guid.NewGuid());
        _memberships.Seed(Membership.AddMember(admin.Id, Guid.NewGuid()));
        var item = _dataItems.Seed(admin.Id);

        var result = await DeleteAccountHandler().Handle(new DeleteAccountCommand(admin.Id, admin.Id), default);

        Assert.Equal(AdminErrorCodes.AccountCannotDeleteOwnAccount, result.Error.Code);
        Assert.False(item.IsDeleted);
        AssertNothingStagedForAccountDelete();
    }

    // Documented in the spec: the account is loaded first, so a requester whose own account is gone gets 404
    [Fact]
    public async Task DeleteAccount_OwnAccountThatNoLongerExists_ReportsNotFound()
    {
        var goneId = Guid.NewGuid();

        var result = await DeleteAccountHandler().Handle(new DeleteAccountCommand(goneId, goneId), default);

        Assert.Equal(AdminErrorCodes.AccountNotFound, result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // --- CreateClearance (since #95 step 3b: an organization admin grants on a policy of their organization) ---

    [Fact]
    public async Task CreateClearance_KnownUserAndPolicy_AddsClearanceAndCommitsOnce()
    {
        var user = SeedMemberUser();
        var policy = _policies.Seed("Handler policy", OrganizationId);

        var result = await CreateClearanceHandler().Handle(ClearanceCommand(user.Id, policy.Id), default);

        Assert.True(result.IsSuccess);
        var added = Assert.Single(_clearances.Added);
        Assert.Equal(
            (result.Value, ClearanceName, user.Id, policy.Id, LevelName, LevelPriority),
            (added.Id, added.Name, added.UserId, added.PolicyId, added.Level.Name, added.Level.Priority));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    // Both user and policy are unknown: since #95 step 3b the policy check comes first (AC2 order)
    [Fact]
    public async Task CreateClearance_UnknownUserAndPolicy_ReportsPolicyFirst()
    {
        var result = await CreateClearanceHandler().Handle(ClearanceCommand(Guid.NewGuid(), Guid.NewGuid()), default);

        AssertClearanceNotCreated(result, AdminErrorCodes.ClearancePolicyNotFound);
    }

    // The user is unknown but a clearance already links the ids: the user check must still win
    [Fact]
    public async Task CreateClearance_UnknownUserAlreadyGranted_ReportsUserFirst()
    {
        var unknownUserId = Guid.NewGuid();
        var policy = _policies.Seed("Handler policy", OrganizationId);
        _clearances.Seed(unknownUserId, policy.Id);

        var result = await CreateClearanceHandler().Handle(ClearanceCommand(unknownUserId, policy.Id), default);

        AssertClearanceNotCreated(result, AdminErrorCodes.ClearanceUserNotFound);
    }

    // The policy is unknown but a clearance already links the ids: the policy check must win
    [Fact]
    public async Task CreateClearance_UnknownPolicyAlreadyGranted_ReportsPolicyBeforeGranted()
    {
        var user = SeedMemberUser();
        var unknownPolicyId = Guid.NewGuid();
        _clearances.Seed(user.Id, unknownPolicyId);

        var result = await CreateClearanceHandler().Handle(ClearanceCommand(user.Id, unknownPolicyId), default);

        AssertClearanceNotCreated(result, AdminErrorCodes.ClearancePolicyNotFound);
    }

    [Fact]
    public async Task CreateClearance_AlreadyGranted_FailsWithoutCommit()
    {
        var user = SeedMemberUser();
        var policy = _policies.Seed("Handler policy", OrganizationId);
        _clearances.Seed(user.Id, policy.Id);

        var result = await CreateClearanceHandler().Handle(ClearanceCommand(user.Id, policy.Id), default);

        AssertClearanceNotCreated(result, AdminErrorCodes.ClearanceAlreadyGranted);
    }

    // --- DeleteClearance ---

    [Fact]
    public async Task DeleteClearance_Existing_RemovesClearanceAndCommitsOnce()
    {
        var policy = _policies.Seed("Handler policy", OrganizationId);
        var clearance = _clearances.Seed(Guid.NewGuid(), policy.Id);

        var result = await DeleteClearanceHandler().Handle(DeleteCommand(clearance.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(clearance.Id, Assert.Single(_clearances.Removed).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task DeleteClearance_Unknown_FailsWithoutCommit()
    {
        var result = await DeleteClearanceHandler().Handle(DeleteCommand(Guid.NewGuid()), default);

        Assert.Equal(AdminErrorCodes.ClearanceNotFound, result.Error.Code);
        Assert.Empty(_clearances.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // --- helpers ---

    private CreateClearanceCommand ClearanceCommand(Guid userId, Guid policyId) =>
        new(OrganizationId, _callerId, userId, policyId, ClearanceName, LevelName, LevelPriority);

    private DeleteClearanceCommand DeleteCommand(Guid clearanceId) => new(OrganizationId, _callerId, clearanceId);

    // A registered user with a membership in the organization (C5)
    private User SeedMemberUser()
    {
        var user = _users.Seed(Email);
        _memberships.Seed(Membership.AddMember(user.Id, OrganizationId));
        return user;
    }

    private void AssertNothingStagedForAccountDelete()
    {
        Assert.Empty(_users.Removed);
        Assert.Empty(_clearances.Removed);
        Assert.Empty(_dataItems.Updated);
        Assert.Empty(_memberships.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    private void AssertClearanceNotCreated(Result<Guid> result, string expectedCode)
    {
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Empty(_clearances.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    private CreateAccountHandler CreateAccountHandler() => new(_users, _hasher, _unitOfWork);

    private DeleteAccountHandler DeleteAccountHandler() => new(_users, _clearances, _dataItems, _memberships, _unitOfWork);

    private CreateClearanceHandler CreateClearanceHandler()
    {
        SeedCallerAsOrgAdmin();
        return new(_memberships, _users, _policies, _clearances, _unitOfWork);
    }

    private DeleteClearanceHandler DeleteClearanceHandler()
    {
        SeedCallerAsOrgAdmin();
        return new(_memberships, _policies, _clearances, _unitOfWork);
    }

    private void SeedCallerAsOrgAdmin() => _memberships.Seed(Membership.AppointOrgAdmin(_callerId, OrganizationId));
}

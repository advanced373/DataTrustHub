using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Administration.RenamePolicy;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// #23 (RenamePolicy) at the handler, built directly with fakes, so no database is involved: the caller check comes
// first (C6), a policy of another organization is not found, P2 looks only at the organization's other policies,
// and a save the unique index rejects (a concurrent rename to the same name) answers the same conflict as the check
public class RenamePolicyHandlerTests
{
    private const string OldName = "Project Apollo";
    private const string NewName = "Project Artemis";
    private const string OtherName = "Project Gemini";
    private const string MemberRole = "Member";
    private const string GuestRole = "Guest";

    public static TheoryData<string> NotOrgAdminRoles => new(MemberRole, GuestRole);

    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly FakeMembershipRepository _memberships = new();
    private readonly FakePolicyRepository _policies = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    // --- AC1 and AC10: renamed, saved once, the id and organization kept ---

    [Fact]
    public async Task RenamePolicy_OrgAdminNewName_RenamesUpdatesAndSavesOnce()
    {
        var callerId = SeedOrgAdmin(_organizationId);
        var policy = _policies.Seed(OldName, _organizationId);
        var id = policy.Id;

        var result = await Handler().Handle(Command(callerId, policy.Id, NewName), default);

        Assert.True(result.IsSuccess);
        Assert.Equal((id, _organizationId, NewName), (policy.Id, policy.OrganizationId, policy.Name));
        Assert.Equal(id, Assert.Single(_policies.Updated).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    // --- AC2: the name of another policy of the same organization (P2) ---

    [Fact]
    public async Task RenamePolicy_NameOfAnotherPolicyOfTheOrganization_FailsWithNameAlreadyInUseAndSavesNothing()
    {
        var callerId = SeedOrgAdmin(_organizationId);
        var policy = _policies.Seed(OldName, _organizationId);
        _policies.Seed(OtherName, _organizationId);

        var result = await Handler().Handle(Command(callerId, policy.Id, OtherName), default);

        AssertConflict(result);
        Assert.Equal((OldName, 0), (policy.Name, _unitOfWork.SaveChangesCalls));
        Assert.Empty(_policies.Updated);
    }

    // --- AC3: P2 is per organization ---

    [Fact]
    public async Task RenamePolicy_NameOfAPolicyOfAnotherOrganization_Succeeds()
    {
        var callerId = SeedOrgAdmin(_organizationId);
        var policy = _policies.Seed(OldName, _organizationId);
        _policies.Seed(OtherName, Guid.NewGuid());

        var result = await Handler().Handle(Command(callerId, policy.Id, OtherName), default);

        Assert.True(result.IsSuccess);
        Assert.Equal((OtherName, 1), (policy.Name, _unitOfWork.SaveChangesCalls));
    }

    // --- AC4: exactly its current name ---

    [Fact]
    public async Task RenamePolicy_ToItsCurrentName_SucceedsAndChangesNothing()
    {
        var callerId = SeedOrgAdmin(_organizationId);
        var policy = _policies.Seed(OldName, _organizationId);

        var result = await Handler().Handle(Command(callerId, policy.Id, OldName), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(OldName, policy.Name);
    }

    // --- AC5: the caller is not an organization admin of the organization ---

    [Theory]
    [MemberData(nameof(NotOrgAdminRoles))]
    public async Task RenamePolicy_CallerNotOrgAdmin_FailsWithOrganizationNotFoundAndKeepsTheName(string role)
    {
        var callerId = SeedMembership(role, _organizationId);
        var policy = _policies.Seed(OldName, _organizationId);

        var result = await Handler().Handle(Command(callerId, policy.Id, NewName), default);

        AssertOrganizationNotFound(result);
        Assert.Equal((OldName, 0), (policy.Name, _unitOfWork.SaveChangesCalls));
    }

    [Fact]
    public async Task RenamePolicy_CallerWithoutMembership_FailsWithOrganizationNotFound()
    {
        var policy = _policies.Seed(OldName, _organizationId);

        var result = await Handler().Handle(Command(Guid.NewGuid(), policy.Id, NewName), default);

        AssertOrganizationNotFound(result);
        Assert.Equal((OldName, 0), (policy.Name, _unitOfWork.SaveChangesCalls));
    }

    [Fact]
    public async Task RenamePolicy_OrgAdminOfAnotherOrganization_FailsWithOrganizationNotFound()
    {
        var callerId = SeedOrgAdmin(Guid.NewGuid());
        var policy = _policies.Seed(OldName, _organizationId);

        var result = await Handler().Handle(Command(callerId, policy.Id, NewName), default);

        AssertOrganizationNotFound(result);
        Assert.Equal(OldName, policy.Name);
    }

    // The caller is not an organization admin and the name is taken: the caller check must win, so P2 reveals nothing
    [Fact]
    public async Task RenamePolicy_CallerNotOrgAdminAndNameTaken_ReportsOrganizationNotFoundFirst()
    {
        var policy = _policies.Seed(OldName, _organizationId);
        _policies.Seed(OtherName, _organizationId);

        var result = await Handler().Handle(Command(Guid.NewGuid(), policy.Id, OtherName), default);

        AssertOrganizationNotFound(result);
    }

    // --- AC6: an unknown policy, or a policy of another organization ---

    [Fact]
    public async Task RenamePolicy_UnknownPolicy_FailsWithPolicyNotFound()
    {
        var callerId = SeedOrgAdmin(_organizationId);

        var result = await Handler().Handle(Command(callerId, Guid.NewGuid(), NewName), default);

        AssertPolicyNotFound(result);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task RenamePolicy_PolicyOfAnotherOrganization_FailsWithPolicyNotFoundAndKeepsTheName()
    {
        var callerId = SeedOrgAdmin(_organizationId);
        var otherPolicy = _policies.Seed(OldName, Guid.NewGuid());

        var result = await Handler().Handle(Command(callerId, otherPolicy.Id, NewName), default);

        AssertPolicyNotFound(result);
        Assert.Equal((OldName, 0), (otherPolicy.Name, _unitOfWork.SaveChangesCalls));
        Assert.Empty(_policies.Updated);
    }

    // A 409 from P2 in the other organization would reveal that its policy exists, so the scope check must come first
    [Fact]
    public async Task RenamePolicy_PolicyOfAnotherOrganizationToATakenName_ReportsPolicyNotFoundFirst()
    {
        var callerId = SeedOrgAdmin(_organizationId);
        var otherOrganizationId = Guid.NewGuid();
        var otherPolicy = _policies.Seed(OldName, otherOrganizationId);
        _policies.Seed(OtherName, otherOrganizationId);

        var result = await Handler().Handle(Command(callerId, otherPolicy.Id, OtherName), default);

        AssertPolicyNotFound(result);
    }

    // --- AC8: two concurrent renames to the same name ---

    [Fact]
    public async Task RenamePolicy_UniqueIndexRejectsSave_FailsWithNameAlreadyInUseConflict()
    {
        var callerId = SeedOrgAdmin(_organizationId);
        var policy = _policies.Seed(OldName, _organizationId);
        var unitOfWork = new UniqueViolationUnitOfWork();

        var result = await Handler(unitOfWork).Handle(Command(callerId, policy.Id, NewName), default);

        AssertConflict(result);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task RenamePolicy_UniqueIndexRejectsSave_FailsWithTheSameErrorAsTheCheck()
    {
        var callerId = SeedOrgAdmin(_organizationId);
        var policy = _policies.Seed(OldName, _organizationId);
        var other = _policies.Seed(OtherName, _organizationId);
        var fromIndex = await Handler(new UniqueViolationUnitOfWork()).Handle(Command(callerId, policy.Id, NewName), default);
        var fromCheck = await Handler().Handle(Command(callerId, policy.Id, OtherName), default);

        Assert.Equal(fromCheck.Error, fromIndex.Error);
    }

    [Fact]
    public async Task RenamePolicy_OtherSaveFailure_IsNotTurnedIntoAConflict()
    {
        var callerId = SeedOrgAdmin(_organizationId);
        var policy = _policies.Seed(OldName, _organizationId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(new FailingUnitOfWork()).Handle(Command(callerId, policy.Id, NewName), default));
    }

    // --- AC11: P2 looks only at the organization's other policies ---

    [Theory]
    [InlineData("PROJECT APOLLO")]
    [InlineData("project apollo")]
    [InlineData(" Project Apollo ")]
    public async Task RenamePolicy_NewSpellingOfItsOwnName_SucceedsAndStoresIt(string newSpelling)
    {
        var callerId = SeedOrgAdmin(_organizationId);
        var policy = _policies.Seed(OldName, _organizationId);

        var result = await Handler().Handle(Command(callerId, policy.Id, newSpelling), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(newSpelling, policy.Name);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    // --- helpers ---

    private RenamePolicyHandler Handler() => Handler(_unitOfWork);

    private RenamePolicyHandler Handler(IUnitOfWork unitOfWork) =>
        new(_memberships, _policies, unitOfWork);

    private RenamePolicyCommand Command(Guid callerId, Guid policyId, string name) =>
        new(_organizationId, callerId, policyId, name);

    private Guid SeedOrgAdmin(Guid organizationId) =>
        _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), organizationId)).UserId;

    private Guid SeedMembership(string role, Guid organizationId) =>
        _memberships.Seed(role == GuestRole
            ? Membership.AddGuest(Guid.NewGuid(), organizationId)
            : Membership.AddMember(Guid.NewGuid(), organizationId)).UserId;

    private static void AssertConflict(Result<Guid> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(
            (ErrorType.Conflict, AdminErrorCodes.PolicyNameAlreadyInUse, AdminErrorMessages.PolicyNameAlreadyInUse),
            (result.Error.Type, result.Error.Code, result.Error.Description));
    }

    private static void AssertOrganizationNotFound(Result<Guid> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal((ErrorType.NotFound, AdminErrorCodes.OrganizationNotFound), (result.Error.Type, result.Error.Code));
    }

    private static void AssertPolicyNotFound(Result<Guid> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal((ErrorType.NotFound, AdminErrorCodes.PolicyNotFound), (result.Error.Type, result.Error.Code));
    }
}

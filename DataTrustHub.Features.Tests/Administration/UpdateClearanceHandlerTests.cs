using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Administration.UpdateClearance;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// #23 (UpdateClearance) at the handler, built directly with fakes, so no database is involved: the check order
// (caller is an organization admin (C6) → clearance found in the organization → not the caller's own (C8)), the
// change itself, and that every refusal changes, stages and commits nothing
public class UpdateClearanceHandlerTests
{
    private const string PolicyName = "Update clearance policy";
    private const string NewName = "Top Secret clearance";
    private const string NewLevelName = "Top Secret";
    private const int NewLevelPriority = 4;
    private const string MemberKind = "Member";
    private const string GuestKind = "Guest";

    private static readonly ClassificationLevel NewLevel = new(NewLevelName, NewLevelPriority);

    public static TheoryData<string> NotOrgAdminMemberships => new(MemberKind, GuestKind);

    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly Guid _callerId = Guid.NewGuid();
    private readonly FakeMembershipRepository _memberships = new();
    private readonly FakePolicyRepository _policies = new();
    private readonly FakeClearanceRepository _clearances = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    public UpdateClearanceHandlerTests() =>
        _memberships.Seed(Membership.AppointOrgAdmin(_callerId, _organizationId));

    // --- AC1: changed, updated and saved once; the user and the policy stay ---

    [Fact]
    public async Task UpdateClearance_AnotherUsersClearance_ChangesUpdatesAndSavesOnce()
    {
        var clearance = SeedClearanceHere(Guid.NewGuid());

        var result = await Handler().Handle(Command(clearance.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal((NewName, NewLevel), (clearance.Name, clearance.Level));
        Assert.Equal(clearance.Id, Assert.Single(_clearances.Updated).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task UpdateClearance_AnotherUsersClearance_KeepsTheUserAndThePolicy()
    {
        var userId = Guid.NewGuid();
        var clearance = SeedClearanceHere(userId);
        var policyId = clearance.PolicyId;

        await Handler().Handle(Command(clearance.Id), default);

        Assert.Equal((userId, policyId), (clearance.UserId, clearance.PolicyId));
    }

    // --- AC3: one's own clearance (C8) ---

    [Fact]
    public async Task UpdateClearance_OwnClearance_FailsWithCannotChangeOwnAndChangesNothing()
    {
        var clearance = SeedClearanceHere(_callerId);

        var result = await Handler().Handle(Command(clearance.Id), default);

        AssertRefused(result, AdminErrorCodes.ClearanceCannotChangeOwn, clearance);
    }

    // --- AC4: the caller check comes first (C6) ---

    [Theory]
    [MemberData(nameof(NotOrgAdminMemberships))]
    public async Task UpdateClearance_CallerNotOrgAdmin_FailsWithOrganizationNotFound(string kind)
    {
        var notOrgAdminId = Guid.NewGuid();
        _memberships.Seed(NotOrgAdminMembership(kind, notOrgAdminId));
        var clearance = SeedClearanceHere(Guid.NewGuid());

        var result = await Handler().Handle(Command(clearance.Id) with { CallerId = notOrgAdminId }, default);

        AssertRefused(result, AdminErrorCodes.OrganizationNotFound, clearance);
    }

    // The clearance is the caller's own and missing elsewhere too: only the caller check may answer
    [Fact]
    public async Task UpdateClearance_CallerOrgAdminOfAnotherOrganizationOnOwnClearance_FailsWithOrganizationNotFound()
    {
        var clearance = SeedClearanceHere(_callerId);

        var result = await Handler().Handle(Command(clearance.Id) with { OrganizationId = Guid.NewGuid() }, default);

        AssertRefused(result, AdminErrorCodes.OrganizationNotFound, clearance);
    }

    // --- AC5: an unknown clearance, or one on a policy of another organization ---

    [Fact]
    public async Task UpdateClearance_UnknownClearance_FailsWithClearanceNotFound()
    {
        var result = await Handler().Handle(Command(Guid.NewGuid()), default);

        Assert.Equal(AdminErrorCodes.ClearanceNotFound, result.Error.Code);
        Assert.Empty(_clearances.Updated);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task UpdateClearance_ClearanceOnAPolicyOfAnotherOrganization_FailsWithClearanceNotFound()
    {
        var foreignPolicy = _policies.Seed(PolicyName, Guid.NewGuid());
        var foreignClearance = _clearances.Seed(Guid.NewGuid(), foreignPolicy.Id);

        var result = await Handler().Handle(Command(foreignClearance.Id), default);

        AssertRefused(result, AdminErrorCodes.ClearanceNotFound, foreignClearance);
    }

    // The foreign clearance is the caller's own: Clearance.NotFound comes before C8, so it is not revealed
    [Fact]
    public async Task UpdateClearance_OwnClearanceOnAPolicyOfAnotherOrganization_FailsWithClearanceNotFound()
    {
        var foreignPolicy = _policies.Seed(PolicyName, Guid.NewGuid());
        var foreignClearance = _clearances.Seed(_callerId, foreignPolicy.Id);

        var result = await Handler().Handle(Command(foreignClearance.Id), default);

        AssertRefused(result, AdminErrorCodes.ClearanceNotFound, foreignClearance);
    }

    // --- AC9: the clearance's current values ---

    [Fact]
    public async Task UpdateClearance_CurrentValues_SucceedsAndKeepsThem()
    {
        var clearance = SeedClearanceHere(Guid.NewGuid());
        var (name, level) = (clearance.Name, clearance.Level);

        var result = await Handler().Handle(
            Command(clearance.Id) with { Name = name, ClassificationLevelName = level.Name, ClassificationLevelPriority = level.Priority },
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal((name, level), (clearance.Name, clearance.Level));
    }

    // --- helpers ---

    private Membership NotOrgAdminMembership(string kind, Guid userId) =>
        kind == MemberKind ? Membership.AddMember(userId, _organizationId) : Membership.AddGuest(userId, _organizationId);

    private Clearance SeedClearanceHere(Guid userId) =>
        _clearances.Seed(userId, _policies.Seed(PolicyName, _organizationId).Id);

    private void AssertRefused(Result<Guid> result, string expectedCode, Clearance clearance)
    {
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.NotEqual((NewName, NewLevel), (clearance.Name, clearance.Level));
        Assert.Empty(_clearances.Updated);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    private UpdateClearanceCommand Command(Guid clearanceId) =>
        new(_organizationId, _callerId, clearanceId, NewName, NewLevelName, NewLevelPriority);

    private UpdateClearanceHandler Handler() => new(_memberships, _policies, _clearances, _unitOfWork);
}

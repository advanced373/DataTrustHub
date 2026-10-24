using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Administration.ChangeMemberRole;
using DataTrustHub.Features.Administration.LeaveOrganization;
using DataTrustHub.Features.Administration.RemoveMember;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// #95 step 2b (ADR-0014): change a member's role, remove a member and leave an organization, built directly
// with fakes, so no database is involved. Covers M2, C7 and the single save (or none on failure)
public class MembershipManagementHandlerTests
{
    private const string MemberRole = "Member";
    private const string GuestRole = "Guest";
    private const string OrgAdminRole = "OrgAdmin";
    private const string PolicyName = "Membership management policy";
    private const string OtherPolicyName = "Other organization policy";
    private const string WrongCaseRole = "orgadmin";
    private const string UnknownRole = "Owner";

    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid OtherOrganizationId = Guid.NewGuid();

    private readonly FakeMembershipRepository _memberships = new();
    private readonly FakePolicyRepository _policies = new();
    private readonly FakeClearanceRepository _clearances = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    // --- ChangeMemberRole ---

    // This is how further organization admins are appointed (ADR-0014)
    [Fact]
    public async Task ChangeMemberRole_PromoteMember_ChangesRoleUpdatesAndCommitsOnce()
    {
        var callerId = SeedOrgAdmin().UserId;
        var member = _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OrganizationId));

        var result = await ChangeRoleHandler().Handle(ChangeRole(callerId, member.UserId, OrgAdminRole), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(MembershipRole.OrgAdmin, member.Role);
        Assert.Equal(member.Id, Assert.Single(_memberships.Updated).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task ChangeMemberRole_DemoteOrgAdminWhileAnotherRemains_ChangesRoleAndCommitsOnce()
    {
        var callerId = SeedOrgAdmin().UserId;
        var otherOrgAdmin = SeedOrgAdmin();

        var result = await ChangeRoleHandler().Handle(ChangeRole(callerId, otherOrgAdmin.UserId, MemberRole), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(MembershipRole.Member, otherOrgAdmin.Role);
        Assert.Equal(otherOrgAdmin.Id, Assert.Single(_memberships.Updated).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task ChangeMemberRole_SelfDemotionWhileAnotherRemains_ChangesRoleAndCommitsOnce()
    {
        var caller = SeedOrgAdmin();
        SeedOrgAdmin();

        var result = await ChangeRoleHandler().Handle(ChangeRole(caller.UserId, caller.UserId, MemberRole), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(MembershipRole.Member, caller.Role);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    // Invariant M2
    [Fact]
    public async Task ChangeMemberRole_SelfDemotionAsLastOrgAdmin_FailsWithLastOrgAdminWithoutCommit()
    {
        var caller = SeedOrgAdmin();

        var result = await ChangeRoleHandler().Handle(ChangeRole(caller.UserId, caller.UserId, MemberRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipLastOrgAdmin, ErrorType.Conflict);
        Assert.Equal(MembershipRole.OrgAdmin, caller.Role);
    }

    // The count is per organization: an organization admin elsewhere does not keep this one's M2
    [Fact]
    public async Task ChangeMemberRole_LastOrgAdminWithOrgAdminInAnotherOrganization_FailsWithLastOrgAdmin()
    {
        var caller = SeedOrgAdmin();
        _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), OtherOrganizationId));

        var result = await ChangeRoleHandler().Handle(ChangeRole(caller.UserId, caller.UserId, MemberRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipLastOrgAdmin, ErrorType.Conflict);
    }

    [Theory]
    [InlineData(MemberRole)]
    [InlineData(OrgAdminRole)]
    public async Task ChangeMemberRole_SameRole_SucceedsWithoutUpdateOrCommit(string role)
    {
        var callerId = SeedOrgAdmin().UserId;
        var target = role == MemberRole
            ? _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OrganizationId))
            : SeedOrgAdmin();

        var result = await ChangeRoleHandler().Handle(ChangeRole(callerId, target.UserId, role), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(_memberships.Updated);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // Same role comes before M2: the last organization admin "changed" to OrgAdmin is a no-op, not a conflict
    [Fact]
    public async Task ChangeMemberRole_LastOrgAdminToOrgAdmin_SucceedsWithoutCommit()
    {
        var caller = SeedOrgAdmin();

        var result = await ChangeRoleHandler().Handle(ChangeRole(caller.UserId, caller.UserId, OrgAdminRole), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(_memberships.Updated);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // Invariant M3, from the entity
    [Theory]
    [InlineData(MemberRole)]
    [InlineData(OrgAdminRole)]
    public async Task ChangeMemberRole_GuestTarget_FailsWithGuestRoleIsFixedWithoutCommit(string role)
    {
        var callerId = SeedOrgAdmin().UserId;
        var guest = _memberships.Seed(Membership.AddGuest(Guid.NewGuid(), OrganizationId));

        var result = await ChangeRoleHandler().Handle(ChangeRole(callerId, guest.UserId, role), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipGuestRoleIsFixed, ErrorType.Conflict);
        Assert.Equal(MembershipRole.Guest, guest.Role);
    }

    [Fact]
    public async Task ChangeMemberRole_TargetNotAMember_FailsWithMembershipNotFound()
    {
        var callerId = SeedOrgAdmin().UserId;
        var elsewhere = _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OtherOrganizationId));

        var result = await ChangeRoleHandler().Handle(ChangeRole(callerId, elsewhere.UserId, OrgAdminRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipNotFound, ErrorType.NotFound);
        Assert.Equal(MembershipRole.Member, elsewhere.Role);
    }

    // The caller check comes first, for every caller who is not an organization admin there
    [Theory]
    [MemberData(nameof(NonOrgAdminCallers))]
    public async Task ChangeMemberRole_CallerNotOrgAdminAndUnknownTarget_FailsWithOrganizationNotFound(string callerKind)
    {
        SeedOrgAdmin();
        var callerId = SeedNonOrgAdminCaller(callerKind);

        var result = await ChangeRoleHandler().Handle(ChangeRole(callerId, Guid.NewGuid(), OrgAdminRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.OrganizationNotFound, ErrorType.NotFound);
    }

    [Theory]
    [MemberData(nameof(NonOrgAdminCallers))]
    public async Task ChangeMemberRole_CallerNotOrgAdminAndExistingMember_FailsWithOrganizationNotFound(string callerKind)
    {
        SeedOrgAdmin();
        var member = _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OrganizationId));
        var callerId = SeedNonOrgAdminCaller(callerKind);

        var result = await ChangeRoleHandler().Handle(ChangeRole(callerId, member.UserId, OrgAdminRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.OrganizationNotFound, ErrorType.NotFound);
        Assert.Equal(MembershipRole.Member, member.Role);
    }

    // Without the validator (handler-level): a name that is no standard role is refused by the handler itself
    [Theory]
    [InlineData(UnknownRole)]
    [InlineData(WrongCaseRole)]
    [InlineData("")]
    [InlineData(null)]
    public async Task ChangeMemberRole_UnknownRoleWithoutValidator_FailsWithUnknownRoleWithoutUpdateOrCommit(string? role)
    {
        var callerId = SeedOrgAdmin().UserId;
        var member = _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OrganizationId));

        var result = await ChangeRoleHandler().Handle(ChangeRole(callerId, member.UserId, role!), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipUnknownRole, ErrorType.Validation);
        Assert.Equal(MembershipRole.Member, member.Role);
    }

    // Without the validator: Guest reaches the entity, which keeps the guest role fixed (M3)
    [Fact]
    public async Task ChangeMemberRole_GuestRequestedForMemberWithoutValidator_FailsWithGuestRoleIsFixedWithoutCommit()
    {
        var callerId = SeedOrgAdmin().UserId;
        var member = _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OrganizationId));

        var result = await ChangeRoleHandler().Handle(ChangeRole(callerId, member.UserId, GuestRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipGuestRoleIsFixed, ErrorType.Conflict);
        Assert.Equal(MembershipRole.Member, member.Role);
    }

    [Fact]
    public async Task ChangeMemberRole_GuestRequestedForOrgAdminWithAnotherWithoutValidator_FailsWithGuestRoleIsFixed()
    {
        var callerId = SeedOrgAdmin().UserId;
        var otherOrgAdmin = SeedOrgAdmin();

        var result = await ChangeRoleHandler().Handle(ChangeRole(callerId, otherOrgAdmin.UserId, GuestRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipGuestRoleIsFixed, ErrorType.Conflict);
        Assert.Equal(MembershipRole.OrgAdmin, otherOrgAdmin.Role);
    }

    // Without the validator: M2 is checked before the entity, so the last organization admin gets LastOrgAdmin
    [Fact]
    public async Task ChangeMemberRole_GuestRequestedForLastOrgAdminWithoutValidator_FailsWithLastOrgAdminWithoutCommit()
    {
        var caller = SeedOrgAdmin();

        var result = await ChangeRoleHandler().Handle(ChangeRole(caller.UserId, caller.UserId, GuestRole), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipLastOrgAdmin, ErrorType.Conflict);
        Assert.Equal(MembershipRole.OrgAdmin, caller.Role);
    }

    // --- RemoveMember ---

    [Fact]
    public async Task RemoveMember_Member_RemovesMembershipAndItsClearancesHereInOneCommit()
    {
        var callerId = SeedOrgAdmin().UserId;
        var member = _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OrganizationId));
        var (here, elsewhere, othersHere) = SeedClearances(member.UserId);

        var result = await RemoveHandler().Handle(new RemoveMemberCommand(OrganizationId, callerId, member.UserId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(member.Id, Assert.Single(_memberships.Removed).Id);
        AssertOnlyRevoked(here, elsewhere, othersHere);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task RemoveMember_Guest_RemovesMembershipAndCommitsOnce()
    {
        var callerId = SeedOrgAdmin().UserId;
        var guest = _memberships.Seed(Membership.AddGuest(Guid.NewGuid(), OrganizationId));

        var result = await RemoveHandler().Handle(new RemoveMemberCommand(OrganizationId, callerId, guest.UserId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(guest.Id, Assert.Single(_memberships.Removed).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task RemoveMember_OrgAdminWhileAnotherRemains_RemovesMembershipAndCommitsOnce()
    {
        var callerId = SeedOrgAdmin().UserId;
        var otherOrgAdmin = SeedOrgAdmin();

        var result = await RemoveHandler().Handle(new RemoveMemberCommand(OrganizationId, callerId, otherOrgAdmin.UserId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(otherOrgAdmin.Id, Assert.Single(_memberships.Removed).Id);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    // Invariant M2, also when the organization admin removes themselves as the last one
    [Fact]
    public async Task RemoveMember_SelfAsLastOrgAdmin_FailsWithLastOrgAdminAndRevokesNothing()
    {
        var caller = SeedOrgAdmin();
        SeedClearances(caller.UserId);

        var result = await RemoveHandler().Handle(new RemoveMemberCommand(OrganizationId, caller.UserId, caller.UserId), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipLastOrgAdmin, ErrorType.Conflict);
        Assert.Empty(_clearances.Removed);
    }

    [Fact]
    public async Task RemoveMember_TargetNotAMember_FailsWithMembershipNotFound()
    {
        var callerId = SeedOrgAdmin().UserId;
        var elsewhere = _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OtherOrganizationId));
        SeedClearances(elsewhere.UserId);

        var result = await RemoveHandler().Handle(new RemoveMemberCommand(OrganizationId, callerId, elsewhere.UserId), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipNotFound, ErrorType.NotFound);
        Assert.Empty(_clearances.Removed);
    }

    [Theory]
    [MemberData(nameof(NonOrgAdminCallers))]
    public async Task RemoveMember_CallerNotOrgAdminAndUnknownTarget_FailsWithOrganizationNotFound(string callerKind)
    {
        SeedOrgAdmin();
        var callerId = SeedNonOrgAdminCaller(callerKind);

        var result = await RemoveHandler().Handle(new RemoveMemberCommand(OrganizationId, callerId, Guid.NewGuid()), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.OrganizationNotFound, ErrorType.NotFound);
    }

    [Theory]
    [MemberData(nameof(NonOrgAdminCallers))]
    public async Task RemoveMember_CallerNotOrgAdminAndExistingMember_FailsWithOrganizationNotFound(string callerKind)
    {
        SeedOrgAdmin();
        var member = _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OrganizationId));
        SeedClearances(member.UserId);
        var callerId = SeedNonOrgAdminCaller(callerKind);

        var result = await RemoveHandler().Handle(new RemoveMemberCommand(OrganizationId, callerId, member.UserId), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.OrganizationNotFound, ErrorType.NotFound);
        Assert.Empty(_clearances.Removed);
    }

    // --- LeaveOrganization ---

    [Theory]
    [MemberData(nameof(LeavingRoles))]
    public async Task LeaveOrganization_MemberGuestOrOrgAdminWithAnother_RemovesMembershipAndClearancesHereInOneCommit(string role)
    {
        SeedOrgAdmin();
        var leaving = _memberships.Seed(NewMembership(role, Guid.NewGuid(), OrganizationId));
        var (here, elsewhere, othersHere) = SeedClearances(leaving.UserId);

        var result = await LeaveHandler().Handle(new LeaveOrganizationCommand(OrganizationId, leaving.UserId), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(leaving.Id, Assert.Single(_memberships.Removed).Id);
        AssertOnlyRevoked(here, elsewhere, othersHere);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task LeaveOrganization_LastOrgAdmin_FailsWithLastOrgAdminAndRevokesNothing()
    {
        var caller = SeedOrgAdmin();
        _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OrganizationId));
        SeedClearances(caller.UserId);

        var result = await LeaveHandler().Handle(new LeaveOrganizationCommand(OrganizationId, caller.UserId), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.MembershipLastOrgAdmin, ErrorType.Conflict);
        Assert.Empty(_clearances.Removed);
    }

    [Fact]
    public async Task LeaveOrganization_CallerNotAMember_FailsWithOrganizationNotFound()
    {
        SeedOrgAdmin();
        var elsewhere = _memberships.Seed(Membership.AddMember(Guid.NewGuid(), OtherOrganizationId));
        SeedClearances(elsewhere.UserId);

        var result = await LeaveHandler().Handle(new LeaveOrganizationCommand(OrganizationId, elsewhere.UserId), default);

        AssertFailedWithoutCommit(result, AdminErrorCodes.OrganizationNotFound, ErrorType.NotFound);
        Assert.Empty(_clearances.Removed);
    }

    // --- data ---

    private const string NoMembership = "NoMembership";
    private const string MemberCaller = "MemberCaller";
    private const string GuestCaller = "GuestCaller";
    private const string OrgAdminElsewhere = "OrgAdminElsewhere";

    public static TheoryData<string> NonOrgAdminCallers => [NoMembership, MemberCaller, GuestCaller, OrgAdminElsewhere];

    public static TheoryData<string> LeavingRoles => [MemberRole, GuestRole, OrgAdminRole];

    // --- helpers ---

    private Membership SeedOrgAdmin() => _memberships.Seed(Membership.AppointOrgAdmin(Guid.NewGuid(), OrganizationId));

    private Guid SeedNonOrgAdminCaller(string callerKind)
    {
        var callerId = Guid.NewGuid();
        if (callerKind == MemberCaller) _memberships.Seed(Membership.AddMember(callerId, OrganizationId));
        if (callerKind == GuestCaller) _memberships.Seed(Membership.AddGuest(callerId, OrganizationId));
        if (callerKind == OrgAdminElsewhere) _memberships.Seed(Membership.AppointOrgAdmin(callerId, OtherOrganizationId));
        return callerId;
    }

    private static Membership NewMembership(string role, Guid userId, Guid organizationId) => role switch
    {
        OrgAdminRole => Membership.AppointOrgAdmin(userId, organizationId),
        GuestRole => Membership.AddGuest(userId, organizationId),
        _ => Membership.AddMember(userId, organizationId)
    };

    // The user's clearance on a policy of this organization, on a policy of another organization, and another
    // user's clearance on this organization's policy
    private (Clearance Here, Clearance Elsewhere, Clearance OthersHere) SeedClearances(Guid userId)
    {
        var policyHere = _policies.Seed(PolicyName, OrganizationId);
        var policyElsewhere = _policies.Seed(OtherPolicyName, OtherOrganizationId);
        return (
            _clearances.Seed(userId, policyHere.Id),
            _clearances.Seed(userId, policyElsewhere.Id),
            _clearances.Seed(Guid.NewGuid(), policyHere.Id));
    }

    // Invariant C7
    private void AssertOnlyRevoked(Clearance here, Clearance elsewhere, Clearance othersHere)
    {
        Assert.Equal(here.Id, Assert.Single(_clearances.Removed).Id);
        Assert.DoesNotContain(elsewhere, _clearances.Removed);
        Assert.DoesNotContain(othersHere, _clearances.Removed);
    }

    private void AssertFailedWithoutCommit(Result<Guid> result, string expectedCode, ErrorType expectedType)
    {
        Assert.Equal((expectedCode, expectedType), (result.Error.Code, result.Error.Type));
        Assert.Empty(_memberships.Updated);
        Assert.Empty(_memberships.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    private static ChangeMemberRoleCommand ChangeRole(Guid callerId, Guid userId, string role) =>
        new(OrganizationId, callerId, userId, role);

    private ChangeMemberRoleHandler ChangeRoleHandler() => new(_memberships, _unitOfWork);

    private RemoveMemberHandler RemoveHandler() => new(_memberships, _policies, _clearances, _unitOfWork);

    private LeaveOrganizationHandler LeaveHandler() => new(_memberships, _policies, _clearances, _unitOfWork);
}

using DataTrustHub.Domain.Memberships;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

// The Membership aggregate (ADR-0014) and its value object MembershipRole, without HTTP or a database
public class MembershipTests
{
    private const string MemberName = "Member";
    private const string GuestName = "Guest";
    private const string OrgAdminName = "OrgAdmin";
    private const string UnknownRoleName = "Owner";
    private const string GuestRoleIsFixedCode = "Membership.GuestRoleIsFixed";

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Fact]
    public void MembershipRole_Standard_HoldsTheThreeRolesByName()
    {
        Assert.Equal([MemberName, GuestName, OrgAdminName], MembershipRole.Standard.Select(role => role.Name));
    }

    [Theory]
    [InlineData(MemberName)]
    [InlineData(GuestName)]
    [InlineData(OrgAdminName)]
    public void MembershipRole_FindStandard_KnownName_ReturnsTheStandardRole(string name)
    {
        Assert.Same(MembershipRole.Standard.Single(role => role.Name == name), MembershipRole.FindStandard(name));
    }

    [Theory]
    [InlineData(UnknownRoleName)]
    [InlineData("member")]
    [InlineData("")]
    public void MembershipRole_FindStandard_UnknownName_ReturnsNull(string name)
    {
        Assert.Null(MembershipRole.FindStandard(name));
    }

    [Fact]
    public void MembershipRole_Helpers_TellOrgAdminAndGuestApart()
    {
        Assert.Equal((true, false), (MembershipRole.OrgAdmin.IsOrgAdmin, MembershipRole.OrgAdmin.IsGuest));
        Assert.Equal((false, true), (MembershipRole.Guest.IsOrgAdmin, MembershipRole.Guest.IsGuest));
        Assert.Equal((false, false), (MembershipRole.Member.IsOrgAdmin, MembershipRole.Member.IsGuest));
    }

    [Fact]
    public void MembershipRole_IsAValueObject_ACopyIsEqualToTheOriginal()
    {
        // A copy is another instance with the same name, so equality is by value, not by reference
        var copy = MembershipRole.Member with { };

        Assert.NotSame(MembershipRole.Member, copy);
        Assert.Equal(MembershipRole.Member, copy);
        Assert.Equal(MembershipRole.Member, MembershipRole.FindStandard(MemberName));
    }

    [Fact]
    public void MembershipRole_StandardRoles_AreDistinctByName()
    {
        Assert.Equal(MembershipRole.Standard.Count, MembershipRole.Standard.Distinct().Count());
        Assert.NotEqual(MembershipRole.Member, MembershipRole.Guest);
        Assert.NotEqual(MembershipRole.Guest, MembershipRole.OrgAdmin);
    }

    [Fact]
    public void MembershipRole_HasNoPublicConstructor_SoTheSetStaysClosed()
    {
        Assert.Empty(typeof(MembershipRole).GetConstructors());
    }

    [Fact]
    public void AddMember_SetsUserOrganizationAndMemberRole()
    {
        AssertCreated(Membership.AddMember(UserId, OrganizationId), MembershipRole.Member);
    }

    [Fact]
    public void AddGuest_SetsUserOrganizationAndGuestRole()
    {
        AssertCreated(Membership.AddGuest(UserId, OrganizationId), MembershipRole.Guest);
    }

    [Fact]
    public void AppointOrgAdmin_SetsUserOrganizationAndOrgAdminRole()
    {
        AssertCreated(Membership.AppointOrgAdmin(UserId, OrganizationId), MembershipRole.OrgAdmin);
    }

    [Fact]
    public void Factories_EachCallGivesANewId()
    {
        var ids = new[]
        {
            Membership.AddMember(UserId, OrganizationId).Id,
            Membership.AddMember(UserId, OrganizationId).Id,
            Membership.AddGuest(UserId, OrganizationId).Id,
            Membership.AppointOrgAdmin(UserId, OrganizationId).Id
        };

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids);
    }

    // Invariant M3 (revised): the guest role is fixed, in both directions
    [Theory]
    [InlineData(GuestName, MemberName)]
    [InlineData(GuestName, OrgAdminName)]
    [InlineData(MemberName, GuestName)]
    [InlineData(OrgAdminName, GuestName)]
    public void ChangeRole_IntoOrOutOfGuest_FailsWithGuestRoleIsFixed(string from, string to)
    {
        var result = Create(MembershipRole.FindStandard(from)!).ChangeRole(MembershipRole.FindStandard(to)!);

        Assert.True(result.IsFailure);
        Assert.Equal(MembershipErrors.GuestRoleIsFixed, result.Error);
        Assert.Equal(GuestRoleIsFixedCode, result.Error.Code);
    }

    [Theory]
    [InlineData(GuestName, MemberName)]
    [InlineData(GuestName, OrgAdminName)]
    [InlineData(MemberName, GuestName)]
    [InlineData(OrgAdminName, GuestName)]
    public void ChangeRole_IntoOrOutOfGuest_LeavesTheRoleUnchanged(string from, string to)
    {
        var original = MembershipRole.FindStandard(from)!;
        var membership = Create(original);

        membership.ChangeRole(MembershipRole.FindStandard(to)!);

        Assert.Equal(original, membership.Role);
    }

    // Regression: the round 1 rule only blocked Guest -> OrgAdmin, so a guest could get there via Member
    [Fact]
    public void ChangeRole_GuestViaMemberToOrgAdmin_FailsAtTheFirstStepAndStaysGuest()
    {
        var guest = Membership.AddGuest(UserId, OrganizationId);

        var toMember = guest.ChangeRole(MembershipRole.Member);
        var toOrgAdmin = guest.ChangeRole(MembershipRole.OrgAdmin);

        Assert.Equal(MembershipErrors.GuestRoleIsFixed, toMember.Error);
        Assert.Equal(MembershipErrors.GuestRoleIsFixed, toOrgAdmin.Error);
        Assert.Equal(MembershipRole.Guest, guest.Role);
    }

    [Fact]
    public void GuestRoleIsFixed_IsAConflict()
    {
        Assert.Equal(ErrorType.Conflict, MembershipErrors.GuestRoleIsFixed.Type);
    }

    [Theory]
    [InlineData(MemberName)]
    [InlineData(GuestName)]
    [InlineData(OrgAdminName)]
    public void ChangeRole_ToTheSameRole_SucceedsAndChangesNothing(string name)
    {
        var role = MembershipRole.FindStandard(name)!;
        var membership = Create(role);
        var before = (membership.Id, membership.UserId, membership.OrganizationId);

        var result = membership.ChangeRole(role);

        Assert.True(result.IsSuccess);
        Assert.Equal(role, membership.Role);
        Assert.Equal(before, (membership.Id, membership.UserId, membership.OrganizationId));
    }

    [Theory]
    [InlineData(MemberName, OrgAdminName)]
    [InlineData(OrgAdminName, MemberName)]
    public void ChangeRole_BetweenMemberAndOrgAdmin_SucceedsAndSetsTheNewRole(string from, string to)
    {
        var membership = Create(MembershipRole.FindStandard(from)!);
        var newRole = MembershipRole.FindStandard(to)!;

        var result = membership.ChangeRole(newRole);

        Assert.True(result.IsSuccess);
        Assert.Equal(newRole, membership.Role);
    }

    [Fact]
    public void ChangeRole_KeepsIdUserAndOrganization()
    {
        var membership = Membership.AddMember(UserId, OrganizationId);
        var id = membership.Id;

        membership.ChangeRole(MembershipRole.OrgAdmin);

        Assert.Equal((id, UserId, OrganizationId), (membership.Id, membership.UserId, membership.OrganizationId));
    }

    // --- helpers ---

    private static Membership Create(MembershipRole role) =>
        role.IsOrgAdmin ? Membership.AppointOrgAdmin(UserId, OrganizationId)
        : role.IsGuest ? Membership.AddGuest(UserId, OrganizationId)
        : Membership.AddMember(UserId, OrganizationId);

    private static void AssertCreated(Membership membership, MembershipRole expectedRole)
    {
        Assert.NotEqual(Guid.Empty, membership.Id);
        Assert.Equal((UserId, OrganizationId, expectedRole), (membership.UserId, membership.OrganizationId, membership.Role));
    }
}

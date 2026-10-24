using System.Net;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC1 of #95 step 2b: an organization admin changes a member's role (ADR-0014). This is how further
// organization admins are appointed; M2 keeps at least one, M3 keeps a guest a guest
public class ChangeMemberRoleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string WrongCaseRole = "orgadmin";
    private const string UnknownRole = "Owner";

    public static TheoryData<string> NotOrgAdminCallers => new(OrganizationCallers.NotOrgAdminKinds);

    // This is how further organization admins are appointed
    [Fact]
    public async Task ChangeMemberRole_PromoteMember_Returns204AndMemberBecomesOrgAdmin()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "role_promote");
        var (memberClient, memberId) = await CreateUserAsync(factory, "role_promote_member");
        var membershipId = await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, memberId, MemberRole);

        var response = await PutMemberRoleAsync(scenario.OrgAdmin, scenario.OrganizationId, memberId, OrgAdminRole);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal((membershipId, OrgAdminRole), await StoredMembershipOfAsync(scenario.OrganizationId, memberId));
        // The new organization admin manages the organization at once, with their existing JWT
        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync(MembersEndpoint(scenario.OrganizationId))).StatusCode);
    }

    [Fact]
    public async Task ChangeMemberRole_DemoteOrgAdminWhileAnotherRemains_Returns204()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "role_demote");
        var (_, secondId) = await OrganizationCallers.AddSecondOrgAdminAsync(factory, scenario, "role_demote_second");

        var response = await PutMemberRoleAsync(scenario.OrgAdmin, scenario.OrganizationId, secondId, MemberRole);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(MemberRole, (await StoredMembershipOfAsync(scenario.OrganizationId, secondId)).Role);
    }

    [Fact]
    public async Task ChangeMemberRole_SelfDemotionWhileAnotherRemains_Returns204AndCallerLosesOrgAdminAccess()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "role_self_demote");
        await OrganizationCallers.AddSecondOrgAdminAsync(factory, scenario, "role_self_demote_second");

        var response = await PutMemberRoleAsync(scenario.OrgAdmin, scenario.OrganizationId, scenario.OrgAdminId, MemberRole);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(MemberRole, (await StoredMembershipOfAsync(scenario.OrganizationId, scenario.OrgAdminId)).Role);
        await AssertOrganizationNotFoundAsync(await scenario.OrgAdmin.GetAsync(MembersEndpoint(scenario.OrganizationId)));
    }

    // Invariant M2
    [Fact]
    public async Task ChangeMemberRole_SelfDemotionAsLastOrgAdmin_Returns409LastOrgAdmin()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "role_last_self");
        var (_, memberId) = await CreateUserAsync(factory, "role_last_self_member");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, memberId, MemberRole);

        var response = await PutMemberRoleAsync(scenario.OrgAdmin, scenario.OrganizationId, scenario.OrgAdminId, MemberRole);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.MembershipLastOrgAdmin, AdminErrorMessages.MembershipLastOrgAdmin);
        Assert.Equal(OrgAdminRole, (await StoredMembershipOfAsync(scenario.OrganizationId, scenario.OrgAdminId)).Role);
    }

    // M2 again after a demotion: the remaining organization admin is now the last one
    [Fact]
    public async Task ChangeMemberRole_DemoteBothOrgAdminsInTurn_SecondReturns409LastOrgAdmin()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "role_last_turn");
        var (second, secondId) = await OrganizationCallers.AddSecondOrgAdminAsync(factory, scenario, "role_last_turn_second");
        await PutMemberRoleAsync(second, scenario.OrganizationId, scenario.OrgAdminId, MemberRole);

        var response = await PutMemberRoleAsync(second, scenario.OrganizationId, secondId, MemberRole);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.MembershipLastOrgAdmin, AdminErrorMessages.MembershipLastOrgAdmin);
        Assert.Equal(OrgAdminRole, (await StoredMembershipOfAsync(scenario.OrganizationId, secondId)).Role);
    }

    [Theory]
    [InlineData(MemberRole)]
    [InlineData(OrgAdminRole)]
    public async Task ChangeMemberRole_SameRole_Returns204AndRoleStays(string role)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"role_same_{role}");
        var targetId = scenario.OrgAdminId;
        if (role == MemberRole)
        {
            (_, targetId) = await CreateUserAsync(factory, $"role_same_{role}_member");
            await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, targetId, MemberRole);
        }

        var response = await PutMemberRoleAsync(scenario.OrgAdmin, scenario.OrganizationId, targetId, role);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(role, (await StoredMembershipOfAsync(scenario.OrganizationId, targetId)).Role);
    }

    // Invariant M3, from the entity
    [Theory]
    [InlineData(MemberRole)]
    [InlineData(OrgAdminRole)]
    public async Task ChangeMemberRole_GuestTarget_Returns409GuestRoleIsFixed(string role)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"role_guest_{role}");
        var (_, guestId) = await CreateUserAsync(factory, $"role_guest_{role}_guest");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, guestId, GuestRole);

        var response = await PutMemberRoleAsync(scenario.OrgAdmin, scenario.OrganizationId, guestId, role);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.MembershipGuestRoleIsFixed, AdminErrorMessages.MembershipGuestRoleIsFixed);
        Assert.Equal(GuestRole, (await StoredMembershipOfAsync(scenario.OrganizationId, guestId)).Role);
    }

    [Fact]
    public async Task ChangeMemberRole_TargetNotAMember_Returns404MembershipNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "role_notmember");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "role_notmember_other");
        var (_, userId) = await CreateUserAsync(factory, "role_notmember_user");
        await AddMemberAsync(other.OrgAdmin, other.OrganizationId, userId, MemberRole);

        var response = await PutMemberRoleAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, OrgAdminRole);

        await AssertMembershipNotFoundAsync(response);
        Assert.Equal(MemberRole, (await StoredMembershipOfAsync(other.OrganizationId, userId)).Role);
    }

    [Fact]
    public async Task ChangeMemberRole_UnknownUser_Returns404MembershipNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "role_nouser");

        var response = await PutMemberRoleAsync(scenario.OrgAdmin, scenario.OrganizationId, Guid.NewGuid(), OrgAdminRole);

        await AssertMembershipNotFoundAsync(response);
    }

    // The caller check comes first, so a caller outside the organization learns nothing about users
    [Theory]
    [MemberData(nameof(NotOrgAdminCallers))]
    public async Task ChangeMemberRole_CallerNotOrgAdminAndUnknownTarget_Returns404OrganizationNotFound(string callerKind)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"role_unknown_{callerKind}");
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, callerKind, $"role_unknown_{callerKind}");

        var response = await PutMemberRoleAsync(caller, scenario.OrganizationId, Guid.NewGuid(), OrgAdminRole);

        await AssertOrganizationNotFoundAsync(response);
    }

    [Theory]
    [MemberData(nameof(NotOrgAdminCallers))]
    public async Task ChangeMemberRole_CallerNotOrgAdmin_Returns404AndRoleStays(string callerKind)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"role_caller_{callerKind}");
        var (_, memberId) = await CreateUserAsync(factory, $"role_caller_{callerKind}_member");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, memberId, MemberRole);
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, callerKind, $"role_caller_{callerKind}");

        var response = await PutMemberRoleAsync(caller, scenario.OrganizationId, memberId, OrgAdminRole);

        await AssertOrganizationNotFoundAsync(response);
        Assert.Equal(MemberRole, (await StoredMembershipOfAsync(scenario.OrganizationId, memberId)).Role);
    }

    [Fact]
    public async Task ChangeMemberRole_UnknownOrganization_Returns404OrganizationNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "role_noorg");

        var response = await PutMemberRoleAsync(scenario.OrgAdmin, Guid.NewGuid(), scenario.OrgAdminId, MemberRole);

        await AssertOrganizationNotFoundAsync(response);
    }

    // The guest role is fixed (M3), so the validator refuses it; role names are case-sensitive
    [Theory]
    [InlineData(GuestRole)]
    [InlineData(WrongCaseRole)]
    [InlineData(UnknownRole)]
    [InlineData("")]
    [InlineData(null)]
    public async Task ChangeMemberRole_RoleNotMemberOrOrgAdmin_Returns400AndRoleStays(string? role)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"role_invalid_{role ?? "null"}");
        var (_, memberId) = await CreateUserAsync(factory, $"role_invalid_{role ?? "null"}_member");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, memberId, MemberRole);

        var response = await PutMemberRoleAsync(scenario.OrgAdmin, scenario.OrganizationId, memberId, role);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(MemberRole, (await StoredMembershipOfAsync(scenario.OrganizationId, memberId)).Role);
    }

    [Fact]
    public async Task ChangeMemberRole_EmptyUserId_Returns400()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "role_empty_user");

        var response = await PutMemberRoleAsync(scenario.OrgAdmin, scenario.OrganizationId, Guid.Empty, MemberRole);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangeMemberRole_Anonymous_Returns401()
    {
        var anonymous = factory.CreateClient();

        var response = await PutMemberRoleAsync(anonymous, Guid.NewGuid(), Guid.NewGuid(), MemberRole);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- helpers ---

    private async Task<(Guid Id, string Role)> StoredMembershipOfAsync(Guid organizationId, Guid userId)
    {
        var stored = Assert.Single(await StoredMembershipsAsync(factory, organizationId), m => m.UserId == userId);
        return (stored.Id, stored.Role);
    }

    private static Task AssertOrganizationNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);

    private static Task AssertMembershipNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.MembershipNotFound, AdminErrorMessages.MembershipNotFound);
}

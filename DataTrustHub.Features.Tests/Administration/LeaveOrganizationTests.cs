using System.Net;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC3 of #95 step 2b: a member, guest or organization admin ends their own membership (ADR-0014). M2 keeps the
// last organization admin; C7 revokes the caller's clearances on the organization's policies in the same save
public class LeaveOrganizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(MemberRole)]
    [InlineData(GuestRole)]
    public async Task LeaveOrganization_MemberOrGuest_Returns204AndRemovesTheMembership(string role)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"leave_{role}");
        var (client, userId) = await CreateUserAsync(factory, $"leave_{role}_user");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, role);

        var response = await LeaveOrganizationAsync(client, scenario.OrganizationId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId);
    }

    [Fact]
    public async Task LeaveOrganization_OrgAdminWhileAnotherRemains_Returns204()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "leave_admin");
        var (_, secondId) = await OrganizationCallers.AddSecondOrgAdminAsync(factory, scenario, "leave_admin_second");

        var response = await LeaveOrganizationAsync(scenario.OrgAdmin, scenario.OrganizationId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(secondId, Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId)).UserId);
    }

    // How the platform admins the AddMemberships migration made organization admins everywhere leave (ADR-0014)
    [Fact]
    public async Task LeaveOrganization_PlatformAdminWithOrgAdminMembership_Returns204()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "leave_platform");
        var platformAdminId = await PlatformAdminIdAsync(factory, "leave_platform");
        await SeedMembershipAsync(factory, Membership.AppointOrgAdmin(platformAdminId, scenario.OrganizationId));

        var response = await LeaveOrganizationAsync(scenario.PlatformAdmin, scenario.OrganizationId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(scenario.OrgAdminId, Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId)).UserId);
    }

    // Invariant C7
    [Fact]
    public async Task LeaveOrganization_CallerWithClearances_RevokesOnlyTheirClearancesOnThisOrganizationsPolicies()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "leave_c7");
        var (client, userId) = await CreateUserAsync(factory, "leave_c7_user");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, GuestRole);
        var clearances = await CreateClearancesAsync(factory, scenario, userId, "leave_c7");

        var response = await LeaveOrganizationAsync(client, scenario.OrganizationId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([clearances.UserClearanceElsewhere], await StoredClearanceIdsOfUserAsync(factory, userId));
        Assert.Equal([clearances.OtherUserClearanceHere], await StoredClearanceIdsOfUserAsync(factory, clearances.OtherUserId));
    }

    [Fact]
    public async Task LeaveOrganization_CallerInTwoOrganizations_KeepsTheOtherMembership()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "leave_two");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "leave_two_other");
        var (client, userId) = await CreateUserAsync(factory, "leave_two_user");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, MemberRole);
        var otherMembershipId = await AddMemberAsync(other.OrgAdmin, other.OrganizationId, userId, MemberRole);

        await LeaveOrganizationAsync(client, scenario.OrganizationId);

        Assert.Equal(otherMembershipId, Assert.Single(await StoredMembershipsOfUserAsync(factory, userId)).Id);
    }

    // Invariant M2; nothing changes
    [Fact]
    public async Task LeaveOrganization_LastOrgAdmin_Returns409LastOrgAdminAndKeepsMembershipAndClearances()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "leave_last");
        var (_, memberId) = await CreateUserAsync(factory, "leave_last_member");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, memberId, MemberRole);
        var clearances = await CreateClearancesAsync(factory, scenario, scenario.OrgAdminId, "leave_last");

        var response = await LeaveOrganizationAsync(scenario.OrgAdmin, scenario.OrganizationId);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.MembershipLastOrgAdmin, AdminErrorMessages.MembershipLastOrgAdmin);
        Assert.Contains(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == scenario.OrgAdminId);
        Assert.Equal(
            new HashSet<Guid> { clearances.UserClearanceHere, clearances.UserClearanceElsewhere },
            (await StoredClearanceIdsOfUserAsync(factory, scenario.OrgAdminId)).ToHashSet());
    }

    [Fact]
    public async Task LeaveOrganization_CallerNotAMember_Returns404OrganizationNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "leave_notmember");
        var (client, userId) = await CreateUserAsync(factory, "leave_notmember_user");
        var clearances = await CreateClearancesAsync(factory, scenario, userId, "leave_notmember");

        var response = await LeaveOrganizationAsync(client, scenario.OrganizationId);

        await AssertOrganizationNotFoundAsync(response);
        Assert.Contains(clearances.UserClearanceHere, await StoredClearanceIdsOfUserAsync(factory, userId));
    }

    // The platform admin who created the organization has no membership in it
    [Fact]
    public async Task LeaveOrganization_PlatformAdminWithoutMembership_Returns404OrganizationNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "leave_platform_none");

        var response = await LeaveOrganizationAsync(scenario.PlatformAdmin, scenario.OrganizationId);

        await AssertOrganizationNotFoundAsync(response);
        Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId));
    }

    [Fact]
    public async Task LeaveOrganization_UnknownOrganization_Returns404OrganizationNotFound()
    {
        var (client, _) = await CreateUserAsync(factory, "leave_noorg_user");

        var response = await LeaveOrganizationAsync(client, Guid.NewGuid());

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task LeaveOrganization_Anonymous_Returns401()
    {
        var anonymous = factory.CreateClient();

        var response = await LeaveOrganizationAsync(anonymous, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Task AssertOrganizationNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);
}

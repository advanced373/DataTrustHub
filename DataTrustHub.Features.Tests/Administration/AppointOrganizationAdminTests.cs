using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC1 of #95 step 2a (revised): the platform admin appoints the first organization admin of an organization,
// never themselves (ADR-0014). Memberships in an organization without an admin are seeded directly
public class AppointOrganizationAdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AppointOrganizationAdmin_UserNotAMember_CreatesOrgAdminMembership()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, "appoint_new");
        var (_, userId) = await CreateUserAsync(factory, "appoint_new_user");

        var response = await PostAppointOrganizationAdminAsync(platformAdmin, organizationId, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var membershipId = (await response.Content.ReadFromJsonAsync<MembershipResponse>())!.MembershipId;
        var stored = Assert.Single(await StoredMembershipsAsync(factory, organizationId));
        Assert.Equal((membershipId, userId, OrgAdminRole), (stored.Id, stored.UserId, stored.Role));
    }

    [Fact]
    public async Task AppointOrganizationAdmin_Member_ChangesRoleOfTheSameMembership()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, "appoint_member");
        var (_, userId) = await CreateUserAsync(factory, "appoint_member_user");
        var memberMembershipId = await SeedMembershipAsync(factory, Membership.AddMember(userId, organizationId));

        var response = await PostAppointOrganizationAdminAsync(platformAdmin, organizationId, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(memberMembershipId, (await response.Content.ReadFromJsonAsync<MembershipResponse>())!.MembershipId);
        var stored = Assert.Single(await StoredMembershipsAsync(factory, organizationId));
        Assert.Equal((memberMembershipId, OrgAdminRole), (stored.Id, stored.Role));
    }

    // Invariant M3: the guest role is fixed
    [Fact]
    public async Task AppointOrganizationAdmin_Guest_Returns409GuestRoleIsFixed()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, "appoint_guest");
        var (_, guestId) = await CreateUserAsync(factory, "appoint_guest_user");
        await SeedMembershipAsync(factory, Membership.AddGuest(guestId, organizationId));

        var response = await PostAppointOrganizationAdminAsync(platformAdmin, organizationId, guestId);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.MembershipGuestRoleIsFixed, AdminErrorMessages.MembershipGuestRoleIsFixed);
        Assert.Equal(GuestRole, Assert.Single(await StoredMembershipsAsync(factory, organizationId)).Role);
    }

    // Replaces the former idempotent 200
    [Fact]
    public async Task AppointOrganizationAdmin_UserAlreadyOrgAdmin_Returns409AlreadyHasOrgAdmin()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "appoint_twice");

        var response = await PostAppointOrganizationAdminAsync(scenario.PlatformAdmin, scenario.OrganizationId, scenario.OrgAdminId);

        await AssertAlreadyHasOrgAdminAsync(response);
        Assert.Equal(OrgAdminRole, Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId)).Role);
    }

    [Fact]
    public async Task AppointOrganizationAdmin_SecondUser_Returns409AlreadyHasOrgAdmin()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "appoint_second");
        var (_, userId) = await CreateUserAsync(factory, "appoint_second_user");

        var response = await PostAppointOrganizationAdminAsync(scenario.PlatformAdmin, scenario.OrganizationId, userId);

        await AssertAlreadyHasOrgAdminAsync(response);
        Assert.DoesNotContain(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId);
    }

    // Further organization admins come from the organization's own admins, not from the platform admin
    [Fact]
    public async Task AppointOrganizationAdmin_MemberOfOrganizationWithOrgAdmin_Returns409AndRoleStays()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "appoint_second_member");
        var (_, userId) = await CreateUserAsync(factory, "appoint_second_member_user");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, MemberRole);

        var response = await PostAppointOrganizationAdminAsync(scenario.PlatformAdmin, scenario.OrganizationId, userId);

        await AssertAlreadyHasOrgAdminAsync(response);
        Assert.Equal(MemberRole, Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId).Role);
    }

    [Fact]
    public async Task AppointOrganizationAdmin_Self_Returns409CannotAppointSelf()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, "appoint_self");
        var platformAdminId = await PlatformAdminIdAsync(factory, "appoint_self");

        var response = await PostAppointOrganizationAdminAsync(platformAdmin, organizationId, platformAdminId);

        await AssertCannotAppointSelfAsync(response);
        Assert.Empty(await StoredMembershipsAsync(factory, organizationId));
    }

    // Order of checks: self comes before "already has an admin"
    [Fact]
    public async Task AppointOrganizationAdmin_SelfInOrganizationWithOrgAdmin_Returns409CannotAppointSelf()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "appoint_self_admin");
        var platformAdminId = await PlatformAdminIdAsync(factory, "appoint_self_admin");

        var response = await PostAppointOrganizationAdminAsync(scenario.PlatformAdmin, scenario.OrganizationId, platformAdminId);

        await AssertCannotAppointSelfAsync(response);
        Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId));
    }

    // Order of checks: the organization comes before self
    [Fact]
    public async Task AppointOrganizationAdmin_UnknownOrganizationAndSelf_Returns404OrganizationNotFound()
    {
        var (platformAdmin, _) = await CreateOrganizationWithoutMembersAsync(factory, "appoint_noorg");
        var platformAdminId = await PlatformAdminIdAsync(factory, "appoint_noorg");

        var response = await PostAppointOrganizationAdminAsync(platformAdmin, Guid.NewGuid(), platformAdminId);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);
    }

    [Fact]
    public async Task AppointOrganizationAdmin_UnknownUser_Returns404UserNotFound()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, "appoint_nouser");

        var response = await PostAppointOrganizationAdminAsync(platformAdmin, organizationId, Guid.NewGuid());

        await AssertUserNotFoundAsync(response);
        Assert.Empty(await StoredMembershipsAsync(factory, organizationId));
    }

    // Order of checks: the user comes before "already has an admin"
    [Fact]
    public async Task AppointOrganizationAdmin_UnknownUserInOrganizationWithOrgAdmin_Returns404UserNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "appoint_nouser_admin");

        var response = await PostAppointOrganizationAdminAsync(scenario.PlatformAdmin, scenario.OrganizationId, Guid.NewGuid());

        await AssertUserNotFoundAsync(response);
    }

    // The rule counts the current organization admins, so an organization that lost its only one can get a new one
    [Fact]
    public async Task AppointOrganizationAdmin_AfterOnlyOrgAdminRemoved_AppointsAgain()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "appoint_again");
        await RemoveMembershipAsync(factory, Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId)).Id);
        var (_, userId) = await CreateUserAsync(factory, "appoint_again_user");

        var response = await PostAppointOrganizationAdminAsync(scenario.PlatformAdmin, scenario.OrganizationId, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId));
        Assert.Equal((userId, OrgAdminRole), (stored.UserId, stored.Role));
    }

    [Fact]
    public async Task AppointOrganizationAdmin_EmptyUserId_Returns400()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, "appoint_empty");

        var response = await PostAppointOrganizationAdminAsync(platformAdmin, organizationId, Guid.Empty);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AppointOrganizationAdmin_RegularUser_Returns403()
    {
        var (user, userId) = await CreateUserAsync(factory, "appoint_regular_user");

        var response = await PostAppointOrganizationAdminAsync(user, Guid.NewGuid(), userId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // An organization admin is a membership role, not the platform Admin role
    [Fact]
    public async Task AppointOrganizationAdmin_OrgAdminOfTheOrganization_Returns403()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "appoint_by_orgadmin");
        var (_, userId) = await CreateUserAsync(factory, "appoint_by_orgadmin_user");

        var response = await PostAppointOrganizationAdminAsync(scenario.OrgAdmin, scenario.OrganizationId, userId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId));
    }

    [Fact]
    public async Task AppointOrganizationAdmin_Anonymous_Returns401()
    {
        var anonymous = factory.CreateClient();

        var response = await PostAppointOrganizationAdminAsync(anonymous, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Appointing gives the platform admin no membership and so no access to the organization
    [Fact]
    public async Task AppointOrganizationAdmin_PlatformAdmin_GainsNoAccessToTheOrganization()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "appoint_no_access");
        var (_, userId) = await CreateUserAsync(factory, "appoint_no_access_user");

        var list = await scenario.PlatformAdmin.GetAsync(MembersEndpoint(scenario.OrganizationId));
        var add = await PostMemberAsync(scenario.PlatformAdmin, scenario.OrganizationId, userId, MemberRole);

        await ErrorAssert.HasErrorAsync(
            list, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);
        await ErrorAssert.HasErrorAsync(
            add, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);
        Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId));
    }

    // --- helpers ---

    private static Task AssertAlreadyHasOrgAdminAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.OrganizationAlreadyHasOrgAdmin, AdminErrorMessages.OrganizationAlreadyHasOrgAdmin);

    private static Task AssertCannotAppointSelfAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.MembershipCannotAppointSelf, AdminErrorMessages.MembershipCannotAppointSelf);

    private static Task AssertUserNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.MembershipUserNotFound, AdminErrorMessages.MembershipUserNotFound);
}

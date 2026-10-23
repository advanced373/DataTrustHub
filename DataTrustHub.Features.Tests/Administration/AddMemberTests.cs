using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC2 of #95 step 2a: an organization admin adds a member or a guest to their own organization (ADR-0014).
// A caller who is not an organization admin there gets 404, so the organization's existence is not revealed
public class AddMemberTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(MemberRole)]
    [InlineData(GuestRole)]
    public async Task AddMember_OrgAdmin_CreatesMembershipWithTheRole(string role)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"add_ok_{role}");
        var (_, userId) = await CreateUserAsync(factory, $"add_ok_{role}_user");

        var response = await PostMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, role);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var membershipId = (await response.Content.ReadFromJsonAsync<MembershipResponse>())!.MembershipId;
        var stored = Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId);
        Assert.Equal((membershipId, role), (stored.Id, stored.Role));
    }

    [Fact]
    public async Task AddMember_CallerNotAMember_Returns404OrganizationNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "add_outsider");
        var (outsider, _) = await CreateUserAsync(factory, "add_outsider_caller");
        var (_, userId) = await CreateUserAsync(factory, "add_outsider_user");

        var response = await PostMemberAsync(outsider, scenario.OrganizationId, userId, MemberRole);

        await AssertOrganizationNotFoundAsync(response);
        Assert.DoesNotContain(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId);
    }

    [Theory]
    [InlineData(MemberRole)]
    [InlineData(GuestRole)]
    public async Task AddMember_CallerIsMemberOrGuest_Returns404OrganizationNotFound(string callerRole)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"add_by_{callerRole}");
        var (caller, callerId) = await CreateUserAsync(factory, $"add_by_{callerRole}_caller");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, callerId, callerRole);
        var (_, userId) = await CreateUserAsync(factory, $"add_by_{callerRole}_user");

        var response = await PostMemberAsync(caller, scenario.OrganizationId, userId, MemberRole);

        await AssertOrganizationNotFoundAsync(response);
        Assert.DoesNotContain(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId);
    }

    [Fact]
    public async Task AddMember_UnknownOrganization_Returns404OrganizationNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "add_noorg");
        var (_, userId) = await CreateUserAsync(factory, "add_noorg_user");

        var response = await PostMemberAsync(scenario.OrgAdmin, Guid.NewGuid(), userId, MemberRole);

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task AddMember_OrgAdminOfAnotherOrganization_Returns404OrganizationNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "add_cross");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "add_cross_other");
        var (_, userId) = await CreateUserAsync(factory, "add_cross_user");

        var response = await PostMemberAsync(other.OrgAdmin, scenario.OrganizationId, userId, MemberRole);

        await AssertOrganizationNotFoundAsync(response);
        Assert.DoesNotContain(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId);
    }

    [Fact]
    public async Task AddMember_PlatformAdminWithoutMembership_Returns404OrganizationNotFound()
    {
        var platformAdmin = await TestUsers.CreateClientAsync(factory, "add_platform@example.com", UserRoles.Admin);
        var organizationId = await CreateOrganizationAsync(platformAdmin, "Add platform organization");
        var (_, userId) = await CreateUserAsync(factory, "add_platform_user");

        var response = await PostMemberAsync(platformAdmin, organizationId, userId, MemberRole);

        await AssertOrganizationNotFoundAsync(response);
        Assert.Empty(await StoredMembershipsAsync(factory, organizationId));
    }

    // Organization admins are appointed, not added; role names are case-sensitive, as stored
    [Theory]
    [InlineData(OrgAdminRole)]
    [InlineData("member")]
    [InlineData("GUEST")]
    [InlineData("Owner")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AddMember_RoleNotMemberOrGuest_Returns400(string? role)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"add_role_{role ?? "null"}_{Guid.NewGuid():N}");
        var (_, userId) = await CreateUserAsync(factory, $"add_role_{Guid.NewGuid():N}_user");

        var response = await PostMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, role);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId);
    }

    [Fact]
    public async Task AddMember_EmptyUserId_Returns400()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "add_empty");

        var response = await PostMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, Guid.Empty, MemberRole);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddMember_UnknownUser_Returns404UserNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "add_nouser");

        var response = await PostMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, Guid.NewGuid(), MemberRole);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.MembershipUserNotFound, AdminErrorMessages.MembershipUserNotFound);
        Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId));
    }

    // Invariant M1: at most one membership per user and organization, whatever the role
    [Theory]
    [InlineData(MemberRole, MemberRole)]
    [InlineData(MemberRole, GuestRole)]
    [InlineData(GuestRole, MemberRole)]
    public async Task AddMember_UserAlreadyMember_Returns409AlreadyMember(string existingRole, string newRole)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"add_dup_{existingRole}_{newRole}");
        var (_, userId) = await CreateUserAsync(factory, $"add_dup_{existingRole}_{newRole}_user");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, existingRole);

        var response = await PostMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, newRole);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.MembershipAlreadyMember, AdminErrorMessages.MembershipAlreadyMember);
        var stored = Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId);
        Assert.Equal(existingRole, stored.Role);
    }

    [Fact]
    public async Task AddMember_OrgAdminAddsThemselves_Returns409AlreadyMember()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "add_self");

        var response = await PostMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, scenario.OrgAdminId, MemberRole);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.MembershipAlreadyMember, AdminErrorMessages.MembershipAlreadyMember);
        Assert.Equal(OrgAdminRole, Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId)).Role);
    }

    [Fact]
    public async Task AddMember_Anonymous_Returns401()
    {
        var anonymous = factory.CreateClient();

        var response = await PostMemberAsync(anonymous, Guid.NewGuid(), Guid.NewGuid(), MemberRole);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- helpers ---

    private static Task AssertOrganizationNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);
}

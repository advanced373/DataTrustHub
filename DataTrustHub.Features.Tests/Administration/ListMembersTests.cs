using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC3 of #95 step 2a: an organization admin lists the memberships of their organization (ADR-0014),
// with the same 404 rule as adding a member
public class ListMembersTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string EmailDomain = "@example.com";

    [Fact]
    public async Task ListMembers_OrgAdmin_ReturnsEveryMembershipWithEmailAndRoleOrderedByEmail()
    {
        // The emails differ in their first letter after the prefix, so the order is the same in every collation
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "list_order_m");
        var (_, zedId) = await CreateUserAsync(factory, "list_order_zed");
        var (_, alphaId) = await CreateUserAsync(factory, "list_order_alpha");
        var zedMembershipId = await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, zedId, MemberRole);
        var alphaMembershipId = await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, alphaId, GuestRole);
        var orgAdminMembershipId = Assert.Single(
            await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == scenario.OrgAdminId).Id;

        var response = await scenario.OrgAdmin.GetAsync(MembersEndpoint(scenario.OrganizationId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var members = await response.Content.ReadFromJsonAsync<List<MemberResponse>>();
        Assert.Equal(
            [
                new MemberResponse(alphaMembershipId, alphaId, $"list_order_alpha{EmailDomain}", GuestRole),
                new MemberResponse(orgAdminMembershipId, scenario.OrgAdminId, $"list_order_m_orgadmin{EmailDomain}", OrgAdminRole),
                new MemberResponse(zedMembershipId, zedId, $"list_order_zed{EmailDomain}", MemberRole)
            ],
            members);
    }

    [Fact]
    public async Task ListMembers_OrgAdmin_ReturnsOnlyThatOrganizationsMemberships()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "list_scope");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "list_scope_other");
        var (_, sharedUserId) = await CreateUserAsync(factory, "list_scope_shared");
        var (_, otherOnlyUserId) = await CreateUserAsync(factory, "list_scope_other_only");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, sharedUserId, MemberRole);
        var otherSharedMembershipId = await AddMemberAsync(other.OrgAdmin, other.OrganizationId, sharedUserId, GuestRole);
        await AddMemberAsync(other.OrgAdmin, other.OrganizationId, otherOnlyUserId, MemberRole);

        var members = (await scenario.OrgAdmin.GetFromJsonAsync<List<MemberResponse>>(MembersEndpoint(scenario.OrganizationId)))!;

        Assert.Equal(
            new HashSet<Guid> { scenario.OrgAdminId, sharedUserId },
            members.Select(m => m.UserId).ToHashSet());
        Assert.Equal(2, members.Count);
        Assert.DoesNotContain(members, m => m.MembershipId == otherSharedMembershipId);
        Assert.Equal(MemberRole, Assert.Single(members, m => m.UserId == sharedUserId).Role);
    }

    [Fact]
    public async Task ListMembers_CallerNotAMember_Returns404OrganizationNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "list_outsider");
        var (outsider, _) = await CreateUserAsync(factory, "list_outsider_caller");

        var response = await outsider.GetAsync(MembersEndpoint(scenario.OrganizationId));

        await AssertOrganizationNotFoundAsync(response);
    }

    [Theory]
    [InlineData(MemberRole)]
    [InlineData(GuestRole)]
    public async Task ListMembers_CallerIsMemberOrGuest_Returns404OrganizationNotFound(string callerRole)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"list_by_{callerRole}");
        var (caller, callerId) = await CreateUserAsync(factory, $"list_by_{callerRole}_caller");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, callerId, callerRole);

        var response = await caller.GetAsync(MembersEndpoint(scenario.OrganizationId));

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task ListMembers_UnknownOrganization_Returns404OrganizationNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "list_noorg");

        var response = await scenario.OrgAdmin.GetAsync(MembersEndpoint(Guid.NewGuid()));

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task ListMembers_OrgAdminOfAnotherOrganization_Returns404OrganizationNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "list_cross");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "list_cross_other");

        var response = await other.OrgAdmin.GetAsync(MembersEndpoint(scenario.OrganizationId));

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task ListMembers_PlatformAdminWithoutMembership_Returns404OrganizationNotFound()
    {
        var platformAdmin = await TestUsers.CreateClientAsync(factory, "list_platform@example.com", UserRoles.Admin);
        var organizationId = await CreateOrganizationAsync(platformAdmin, "List platform organization");

        var response = await platformAdmin.GetAsync(MembersEndpoint(organizationId));

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task ListMembers_Anonymous_Returns401()
    {
        var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(MembersEndpoint(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- helpers ---

    private static Task AssertOrganizationNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);

    private record MemberResponse(Guid MembershipId, Guid UserId, string Email, string Role);
}

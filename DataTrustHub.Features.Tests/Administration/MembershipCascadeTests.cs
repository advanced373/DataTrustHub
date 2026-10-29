using System.Net;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC5 and AC6 of #95 step 2b: deleting an account or an organization ends the memberships involved, in the
// same save (ADR-0014). M2 does not block deleting an account
public class MembershipCascadeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AccountsEndpoint = "/admin/accounts";

    // --- delete account (AC5) ---

    [Fact]
    public async Task DeleteAccount_UserInSeveralOrganizations_RemovesEveryMembershipOfTheUser()
    {
        var first = await CreateOrganizationWithOrgAdminAsync(factory, "cascade_account_a");
        var second = await CreateOrganizationWithOrgAdminAsync(factory, "cascade_account_b");
        var (_, userId) = await CreateUserAsync(factory, "cascade_account_user");
        await AddMemberAsync(first.OrgAdmin, first.OrganizationId, userId, MemberRole);
        await AddMemberAsync(second.OrgAdmin, second.OrganizationId, userId, GuestRole);

        var response = await first.PlatformAdmin.DeleteAsync(AccountEndpoint(userId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await StoredMembershipsOfUserAsync(factory, userId));
        Assert.Equal(first.OrgAdminId, Assert.Single(await StoredMembershipsAsync(factory, first.OrganizationId)).UserId);
        Assert.Equal(second.OrgAdminId, Assert.Single(await StoredMembershipsAsync(factory, second.OrganizationId)).UserId);
    }

    // M2 does not block it; regression from the 2a review: no orphaned membership keeps counting as an
    // organization admin, so the platform admin can appoint a new one (no Organization.AlreadyHasOrgAdmin)
    [Fact]
    public async Task DeleteAccount_OnlyOrgAdmin_Returns204AndOrganizationCanGetANewOrgAdmin()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "cascade_reappoint");
        var (_, newAdminId) = await CreateUserAsync(factory, "cascade_reappoint_new");

        var deleted = await scenario.PlatformAdmin.DeleteAsync(AccountEndpoint(scenario.OrgAdminId));
        var appointed = await PostAppointOrganizationAdminAsync(scenario.PlatformAdmin, scenario.OrganizationId, newAdminId);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, appointed.StatusCode);
        var stored = Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId));
        Assert.Equal((newAdminId, OrgAdminRole), (stored.UserId, stored.Role));
    }

    // Regression from the 2a review: the deleted user's still-valid JWT no longer passes the OrgAdmin check
    [Fact]
    public async Task DeleteAccount_OrgAdmin_DeletedUsersTokenGets404FromOrganizationAdminUseCases()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "cascade_token");
        await OrganizationCallers.AddSecondOrgAdminAsync(factory, scenario, "cascade_token_second");
        var (_, userId) = await CreateUserAsync(factory, "cascade_token_user");

        await scenario.PlatformAdmin.DeleteAsync(AccountEndpoint(scenario.OrgAdminId));
        var list = await scenario.OrgAdmin.GetAsync(MembersEndpoint(scenario.OrganizationId));
        var add = await PostMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, MemberRole);

        await AssertOrganizationNotFoundAsync(list);
        await AssertOrganizationNotFoundAsync(add);
        Assert.DoesNotContain(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId);
    }

    // --- delete organization (AC6) ---

    [Fact]
    public async Task DeleteOrganization_WithMemberships_RemovesThemAndKeepsOtherOrganizationsMemberships()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "cascade_org");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "cascade_org_other");
        var (_, userId) = await CreateUserAsync(factory, "cascade_org_user");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, MemberRole);
        var otherMembershipId = await AddMemberAsync(other.OrgAdmin, other.OrganizationId, userId, GuestRole);

        var response = await scenario.PlatformAdmin.DeleteAsync(OrganizationEndpoint(scenario.OrganizationId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await StoredMembershipsAsync(factory, scenario.OrganizationId));
        Assert.Equal(otherMembershipId, Assert.Single(await StoredMembershipsOfUserAsync(factory, userId)).Id);
        Assert.Equal(2, (await StoredMembershipsAsync(factory, other.OrganizationId)).Count);
    }

    // O2 still refuses an organization with policies, and its memberships stay
    [Fact]
    public async Task DeleteOrganization_WithPolicies_Returns409AndKeepsMemberships()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "cascade_org_policies");
        await CreatePolicyAsync(scenario.OrgAdmin, "cascade_org_policies policy", scenario.OrganizationId);

        var response = await scenario.PlatformAdmin.DeleteAsync(OrganizationEndpoint(scenario.OrganizationId));

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.OrganizationHasPolicies, AdminErrorMessages.OrganizationHasPolicies);
        Assert.Equal(scenario.OrgAdminId, Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId)).UserId);
    }

    // --- helpers ---

    private static string AccountEndpoint(Guid accountId) => $"{AccountsEndpoint}/{accountId}";

    private static string OrganizationEndpoint(Guid organizationId) => $"{OrganizationsEndpoint}/{organizationId}";

    private static Task AssertOrganizationNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);
}

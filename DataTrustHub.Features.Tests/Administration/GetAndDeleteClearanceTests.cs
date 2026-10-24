using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.ClearanceSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC4 of #95 step 3b (ADR-0014, C6): an organization admin reads and revokes the clearances on the policies of
// their organization; a clearance that is missing or on a policy of another organization is not found.
// Who may call (AC1) is covered in ClearanceAccessTests.
public class GetAndDeleteClearanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GetClearance_Existing_ReturnsIt()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_get");
        var clearanceId = await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId);

        var clearance = await grant.OrgAdmin.GetFromJsonAsync<ClearanceResponse>(ClearanceEndpoint(grant.OrganizationId, clearanceId));

        Assert.Equal(
            new ClearanceResponse(
                clearanceId, $"{DefaultLevelName} clearance", grant.MemberId, grant.PolicyId, DefaultLevelName, DefaultLevelPriority),
            clearance);
    }

    [Fact]
    public async Task GetAndDeleteClearance_Missing_Return404ClearanceNotFound()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_missing");
        var missingRoute = ClearanceEndpoint(grant.OrganizationId, Guid.NewGuid());

        await AssertClearanceNotFoundAsync(await grant.OrgAdmin.GetAsync(missingRoute));
        await AssertClearanceNotFoundAsync(await grant.OrgAdmin.DeleteAsync(missingRoute));
    }

    // The caller is an organization admin of both organizations, so only the scope of the route can refuse
    [Fact]
    public async Task GetClearance_ClearanceOfAnotherOrganizationThroughMyRoute_Returns404ClearanceNotFound()
    {
        var (grant, foreignClearanceId) = await ArrangeForeignClearanceAsync("clearance_get_foreign");

        var response = await grant.OrgAdmin.GetAsync(ClearanceEndpoint(grant.OrganizationId, foreignClearanceId));

        await AssertClearanceNotFoundAsync(response);
    }

    [Fact]
    public async Task DeleteClearance_ClearanceOfAnotherOrganizationThroughMyRoute_Returns404AndKeepsIt()
    {
        var (grant, foreignClearanceId) = await ArrangeForeignClearanceAsync("clearance_delete_foreign");

        var response = await grant.OrgAdmin.DeleteAsync(ClearanceEndpoint(grant.OrganizationId, foreignClearanceId));

        await AssertClearanceNotFoundAsync(response);
        Assert.Contains(await StoredClearancesAsync(factory), c => c.Id == foreignClearanceId);
    }

    [Fact]
    public async Task DeleteClearance_Existing_Returns204_AndUnblocksPolicyDeletion()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_delete");
        var clearanceId = await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId);
        var policyRoute = PolicyEndpoint(grant.OrganizationId, grant.PolicyId);
        var blocked = await grant.OrgAdmin.DeleteAsync(policyRoute);

        var response = await grant.OrgAdmin.DeleteAsync(ClearanceEndpoint(grant.OrganizationId, clearanceId));

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertClearanceNotFoundAsync(await grant.OrgAdmin.GetAsync(ClearanceEndpoint(grant.OrganizationId, clearanceId)));
        Assert.Equal(HttpStatusCode.NoContent, (await grant.OrgAdmin.DeleteAsync(policyRoute)).StatusCode);
    }

    [Fact]
    public async Task DeleteClearance_Existing_KeepsTheClassificationLevel()
    {
        const string levelName = "KeptAfterDelete";
        const int levelPriority = 9;
        var grant = await CreateGrantScenarioAsync(factory, "clearance_keep_level");
        var clearanceId = await CreateClearanceAsync(
            grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId, levelName, levelPriority);

        var response = await grant.OrgAdmin.DeleteAsync(ClearanceEndpoint(grant.OrganizationId, clearanceId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        Assert.False(await db.Clearances.AnyAsync(c => c.Id == clearanceId));
        Assert.True(await db.ClassificationLevels.AnyAsync(l => l.Name == levelName && l.Priority == levelPriority));
    }

    // Revoking one's own clearance only removes access (ADR-0014); it was granted by another organization admin (C8)
    [Fact]
    public async Task DeleteClearance_OwnClearance_Returns204AndRemovesIt()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_delete_own");
        var (second, _) = await OrganizationCallers.AddSecondOrgAdminAsync(factory, grant.Organization, "clearance_delete_own_second");
        var ownClearanceId = await CreateClearanceAsync(second, grant.OrganizationId, grant.Organization.OrgAdminId, grant.PolicyId);

        var response = await grant.OrgAdmin.DeleteAsync(ClearanceEndpoint(grant.OrganizationId, ownClearanceId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(await StoredClearancesAsync(factory), c => c.Id == ownClearanceId);
    }

    // Another organization admin of the same organization reads and revokes a clearance they did not grant
    [Fact]
    public async Task GetAndDeleteClearance_ByAnotherOrgAdminOfTheOrganization_Succeed()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_other_admin");
        var (second, _) = await OrganizationCallers.AddSecondOrgAdminAsync(factory, grant.Organization, "clearance_other_admin_second");
        var clearanceId = await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId);
        var route = ClearanceEndpoint(grant.OrganizationId, clearanceId);

        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await second.DeleteAsync(route)).StatusCode);
    }

    // --- helpers ---

    // The caller is organization admin of the scenario's organization and of another one, where the member holds a clearance
    private async Task<(GrantScenario Grant, Guid ForeignClearanceId)> ArrangeForeignClearanceAsync(string prefix)
    {
        var grant = await CreateGrantScenarioAsync(factory, prefix);
        var other = await CreateOrganizationWithOrgAdminAsync(factory, $"{prefix}_other");
        var foreignPolicyId = await CreatePolicyAsync(other.OrgAdmin, $"{prefix} other policy", other.OrganizationId);
        var foreignClearanceId = await AddMemberAndGrantClearanceAsync(
            other.OrgAdmin, other.OrganizationId, grant.MemberId, foreignPolicyId, DefaultLevelName, DefaultLevelPriority);
        await SeedMembershipAsync(factory, Membership.AppointOrgAdmin(grant.Organization.OrgAdminId, other.OrganizationId));
        return (grant, foreignClearanceId);
    }

    private static Task AssertClearanceNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound, AdminErrorCodes.ClearanceNotFound, AdminErrorMessages.ClearanceNotFound);

    private record ClearanceResponse(
        Guid Id, string Name, Guid UserId, Guid PolicyId, string ClassificationLevelName, int ClassificationLevelPriority);
}

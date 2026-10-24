using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC3, AC4 and AC5 of #95 step 3a: an organization admin lists, reads and deletes the policies of their
// organization; a policy of another organization is not found through it (C6, P3)
public class ManagePoliciesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // --- ListPolicies (AC3) ---

    [Fact]
    public async Task ListPolicies_OrgAdmin_ReturnsOnlyThatOrganizationsPolicies()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_list_scope");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "policies_list_scope_other");
        var policyA = await CreatePolicyAsync(scenario.OrgAdmin, "Policy A", scenario.OrganizationId);
        await CreatePolicyAsync(other.OrgAdmin, "Policy B", other.OrganizationId);

        var listed = await scenario.OrgAdmin.GetFromJsonAsync<List<PolicyResponse>>(PoliciesEndpoint(scenario.OrganizationId));

        Assert.Equal([new PolicyResponse(policyA, "Policy A", scenario.OrganizationId)], listed);
    }

    [Fact]
    public async Task ListPolicies_OrgAdmin_ReturnsPoliciesOrderedByName()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_list_order");
        var zed = await CreatePolicyAsync(scenario.OrgAdmin, "Z policy", scenario.OrganizationId);
        var alpha = await CreatePolicyAsync(scenario.OrgAdmin, "A policy", scenario.OrganizationId);

        var listed = await scenario.OrgAdmin.GetFromJsonAsync<List<PolicyResponse>>(PoliciesEndpoint(scenario.OrganizationId));

        Assert.Equal([alpha, zed], listed!.Select(p => p.Id).ToArray());
    }

    [Fact]
    public async Task ListPolicies_OrganizationWithoutPolicies_Returns200Empty()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_list_empty");

        var response = await scenario.OrgAdmin.GetAsync(PoliciesEndpoint(scenario.OrganizationId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<PolicyResponse>>())!);
    }

    // The old ?organizationId= filter is gone: the route alone scopes the list
    [Fact]
    public async Task ListPolicies_QueryWithAnotherOrganizationId_StillListsTheRouteOrganization()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_list_query");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "policies_list_query_other");
        var policyHere = await CreatePolicyAsync(scenario.OrgAdmin, "Query policy here", scenario.OrganizationId);
        await CreatePolicyAsync(other.OrgAdmin, "Query policy elsewhere", other.OrganizationId);

        var listed = await scenario.OrgAdmin.GetFromJsonAsync<List<PolicyResponse>>(
            $"{PoliciesEndpoint(scenario.OrganizationId)}?organizationId={other.OrganizationId}");

        Assert.Equal([policyHere], listed!.Select(p => p.Id).ToArray());
    }

    // --- GetPolicy (AC4) ---

    [Fact]
    public async Task GetPolicy_OrgAdmin_ReturnsIt()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_get");
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, "Readable policy", scenario.OrganizationId);

        var found = await scenario.OrgAdmin.GetFromJsonAsync<PolicyResponse>(PolicyEndpoint(scenario.OrganizationId, policyId));

        Assert.Equal(new PolicyResponse(policyId, "Readable policy", scenario.OrganizationId), found);
    }

    [Fact]
    public async Task GetPolicy_Missing_Returns404PolicyNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_get_missing");

        var response = await scenario.OrgAdmin.GetAsync(PolicyEndpoint(scenario.OrganizationId, Guid.NewGuid()));

        await AssertPolicyNotFoundAsync(response);
    }

    // Never reveal another organization's policy
    [Fact]
    public async Task GetPolicy_OfAnotherOrganizationThroughOwnRoute_Returns404PolicyNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_get_other");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "policies_get_other_org");
        var otherPolicy = await CreatePolicyAsync(other.OrgAdmin, "Other organization policy", other.OrganizationId);

        var response = await scenario.OrgAdmin.GetAsync(PolicyEndpoint(scenario.OrganizationId, otherPolicy));

        await AssertPolicyNotFoundAsync(response);
    }

    // --- DeletePolicy (AC4) ---

    [Fact]
    public async Task DeletePolicy_WithoutClearances_Returns204AndRemovesIt()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_delete");
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, "Disposable policy", scenario.OrganizationId);

        var response = await scenario.OrgAdmin.DeleteAsync(PolicyEndpoint(scenario.OrganizationId, policyId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(await StoredPoliciesAsync(factory), p => p.Id == policyId);
        await AssertPolicyNotFoundAsync(await scenario.OrgAdmin.GetAsync(PolicyEndpoint(scenario.OrganizationId, policyId)));
    }

    // Invariant P3
    [Fact]
    public async Task DeletePolicy_WithClearances_Returns409WithCode()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_conflict");
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, "Policy with clearance", scenario.OrganizationId);
        await AddClearanceAsync(policyId);

        var response = await scenario.OrgAdmin.DeleteAsync(PolicyEndpoint(scenario.OrganizationId, policyId));

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.PolicyHasClearances, AdminErrorMessages.PolicyHasClearances);
    }

    [Fact]
    public async Task DeletePolicy_WithClearances_KeepsPolicy()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_conflict_kept");
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, "Kept policy with clearance", scenario.OrganizationId);
        await AddClearanceAsync(policyId);

        await scenario.OrgAdmin.DeleteAsync(PolicyEndpoint(scenario.OrganizationId, policyId));

        Assert.Contains(await StoredPoliciesAsync(factory), p => p.Id == policyId);
    }

    [Fact]
    public async Task DeletePolicy_Missing_Returns404PolicyNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_delete_missing");

        var response = await scenario.OrgAdmin.DeleteAsync(PolicyEndpoint(scenario.OrganizationId, Guid.NewGuid()));

        await AssertPolicyNotFoundAsync(response);
    }

    [Fact]
    public async Task DeletePolicy_OfAnotherOrganizationThroughOwnRoute_Returns404PolicyNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_delete_other");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "policies_delete_other_org");
        var otherPolicy = await CreatePolicyAsync(other.OrgAdmin, "Other organization disposable", other.OrganizationId);

        var response = await scenario.OrgAdmin.DeleteAsync(PolicyEndpoint(scenario.OrganizationId, otherPolicy));

        await AssertPolicyNotFoundAsync(response);
    }

    [Fact]
    public async Task DeletePolicy_OfAnotherOrganizationThroughOwnRoute_KeepsPolicy()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_delete_other_kept");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "policies_delete_other_kept_org");
        var otherPolicy = await CreatePolicyAsync(other.OrgAdmin, "Other organization kept", other.OrganizationId);

        await scenario.OrgAdmin.DeleteAsync(PolicyEndpoint(scenario.OrganizationId, otherPolicy));

        var stored = Assert.Single(await StoredPoliciesAsync(factory), p => p.Id == otherPolicy);
        Assert.Equal(other.OrganizationId, stored.OrganizationId);
    }

    // A 409 from P3 would reveal that the other organization's policy exists, so the scope check must come first
    [Fact]
    public async Task DeletePolicy_OfAnotherOrganizationWithClearances_Returns404PolicyNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_delete_other_cleared");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "policies_delete_other_cleared_org");
        var otherPolicy = await CreatePolicyAsync(other.OrgAdmin, "Other organization cleared", other.OrganizationId);
        await AddClearanceAsync(otherPolicy);

        var response = await scenario.OrgAdmin.DeleteAsync(PolicyEndpoint(scenario.OrganizationId, otherPolicy));

        await AssertPolicyNotFoundAsync(response);
    }

    // --- AC5: the platform admin's /admin/policies routes are gone ---

    [Fact]
    public async Task LegacyPolicyRoutes_PlatformAdmin_Return404FromRouting()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_legacy_routes");
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, "Legacy route policy", scenario.OrganizationId);
        var admin = scenario.PlatformAdmin;
        var legacyPolicyRoute = $"{LegacyPoliciesEndpoint}/{policyId}";

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(LegacyPoliciesEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync(
            LegacyPoliciesEndpoint, new { Name = "Legacy policy", scenario.OrganizationId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(legacyPolicyRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync(legacyPolicyRoute)).StatusCode);
    }

    [Fact]
    public async Task LegacyPolicyRoutes_PlatformAdmin_ChangeNothing()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policies_legacy_unchanged");
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, "Legacy kept policy", scenario.OrganizationId);
        var admin = scenario.PlatformAdmin;

        await admin.PostAsJsonAsync(LegacyPoliciesEndpoint, new { Name = "Legacy created policy", scenario.OrganizationId });
        await admin.DeleteAsync($"{LegacyPoliciesEndpoint}/{policyId}");

        var stored = await StoredPoliciesAsync(factory);
        Assert.Contains(stored, p => p.Id == policyId);
        Assert.DoesNotContain(stored, p => p.Name == "Legacy created policy");
    }

    // --- helpers ---

    private static Task AssertPolicyNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound, AdminErrorCodes.PolicyNotFound, AdminErrorMessages.PolicyNotFound);

    // Stored directly: the clearance use cases are not under test here
    private async Task AddClearanceAsync(Guid policyId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        db.Clearances.Add(new DbClearance
        {
            Id = Guid.NewGuid(),
            Name = "Blocking clearance",
            PolicyId = policyId,
            UserId = Guid.NewGuid(),
            ClassificationLevel = new DbClassificationLevel { Name = "Secret", Priority = 2 }
        });
        await db.SaveChangesAsync();
    }

    private record PolicyResponse(Guid Id, string Name, Guid OrganizationId);
}

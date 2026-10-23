using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class ManageOrganizationsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string OrganizationsEndpoint = "/admin/organizations";
    // Pre-rename route, kept only to prove it no longer exists (issue #91)
    private const string LegacyGroupsEndpoint = "/admin/groups";

    [Fact]
    public async Task ListAndGetOrganization_Admin_ReturnsCreatedOrganization()
    {
        var admin = await CreateAdminAsync("organizations_list_admin@example.com");
        var organizationId = await CreateOrganizationAsync(admin, "Listed organization");

        var organizations = await admin.GetFromJsonAsync<List<OrganizationResponse>>(OrganizationsEndpoint);
        var organization = await admin.GetFromJsonAsync<OrganizationResponse>($"{OrganizationsEndpoint}/{organizationId}");

        Assert.Contains(organizations!, o => o.Id == organizationId && o.Name == "Listed organization");
        Assert.Equal("Listed organization", organization!.Name);
    }

    [Fact]
    public async Task GetOrganization_Missing_Returns404()
    {
        var admin = await CreateAdminAsync("organizations_missing_admin@example.com");

        var response = await admin.GetAsync($"{OrganizationsEndpoint}/{Guid.NewGuid()}");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);
    }

    // The pre-rename routes are gone: an admin gets 404, not the old slice
    [Fact]
    public async Task LegacyGroupRoutes_Admin_Return404()
    {
        var admin = await CreateAdminAsync("organizations_legacy_routes@example.com");
        var someId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(LegacyGroupsEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync(LegacyGroupsEndpoint, new { Name = "Legacy route organization" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{LegacyGroupsEndpoint}/{someId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{LegacyGroupsEndpoint}/{someId}")).StatusCode);
    }

    [Fact]
    public async Task LegacyGroupsPost_Admin_DoesNotCreateOrganization()
    {
        const string name = "Legacy post organization";
        var admin = await CreateAdminAsync("organizations_legacy_post@example.com");

        await admin.PostAsJsonAsync(LegacyGroupsEndpoint, new { Name = name });

        var organizations = await admin.GetFromJsonAsync<List<OrganizationResponse>>(OrganizationsEndpoint);
        Assert.DoesNotContain(organizations!, o => o.Name == name);
    }

    [Fact]
    public async Task DeleteOrganization_WithoutPolicies_Returns204AndRemovesIt()
    {
        var admin = await CreateAdminAsync("organizations_delete_admin@example.com");
        var organizationId = await CreateOrganizationAsync(admin, "Disposable organization");

        var response = await admin.DeleteAsync($"{OrganizationsEndpoint}/{organizationId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{OrganizationsEndpoint}/{organizationId}")).StatusCode);
    }

    [Fact]
    public async Task DeleteOrganization_WithPolicies_Returns409WithCode()
    {
        var admin = await CreateAdminAsync("organizations_conflict_admin@example.com");
        var (organizationId, orgAdmin) =
            await AdminSetup.CreateOrganizationWithOrgAdminAsync(factory, admin, "Organization with policy");
        await AdminSetup.CreatePolicyAsync(orgAdmin, "Blocking policy", organizationId);

        var response = await admin.DeleteAsync($"{OrganizationsEndpoint}/{organizationId}");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.OrganizationHasPolicies, AdminErrorMessages.OrganizationHasPolicies);
    }

    [Fact]
    public async Task DeleteOrganization_Missing_Returns404WithCode()
    {
        var admin = await CreateAdminAsync("organizations_delete_missing@example.com");

        var response = await admin.DeleteAsync($"{OrganizationsEndpoint}/{Guid.NewGuid()}");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);
    }

    [Fact]
    public async Task OrganizationEndpoints_RegularUser_Return403()
    {
        var user = await TestUsers.CreateClientAsync(factory, "organizations_regular@example.com");
        var someId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(OrganizationsEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"{OrganizationsEndpoint}/{someId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"{OrganizationsEndpoint}/{someId}")).StatusCode);
    }

    // --- helpers ---

    private Task<HttpClient> CreateAdminAsync(string email) =>
        TestUsers.CreateClientAsync(factory, email, UserRoles.Admin);

    private static async Task<Guid> CreateOrganizationAsync(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync(OrganizationsEndpoint, new { Name = name });
        return (await response.Content.ReadFromJsonAsync<CreateOrganizationResponse>())!.OrganizationId;
    }

    private record OrganizationResponse(Guid Id, string Name);
    private record CreateOrganizationResponse(Guid OrganizationId);
}

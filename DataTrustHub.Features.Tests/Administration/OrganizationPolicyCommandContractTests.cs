using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// ADR-0012 step 3b-1: organization and policy command behaviour not covered by the per-slice tests
// (state kept on a conflict, per-organization name uniqueness, authorization of the routes).
// Since #95 step 3a the policy commands are called by an organization admin of the policy's organization.
public class OrganizationPolicyCommandContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // The same user is organization admin of both organizations, so only the organization differs (P2 is per organization)
    [Fact]
    public async Task CreatePolicy_SameNameInOtherOrganization_Returns200AndStoresPolicy()
    {
        var admin = await CreateAdminAsync("contract_policy_otherorganization@example.com");
        var (orgAdmin, orgAdminId) = await CreateUserAsync(factory, "contract_policy_otherorganization_orgadmin");
        var organizationA = await CreateOrganizationAsync(admin, "Contract shared name organization A");
        var organizationB = await CreateOrganizationAsync(admin, "Contract shared name organization B");
        await AppointOrganizationAdminAsync(admin, organizationA, orgAdminId);
        await AppointOrganizationAdminAsync(admin, organizationB, orgAdminId);
        await PostPolicyAsync(orgAdmin, organizationA, "Contract shared policy");

        var response = await PostPolicyAsync(orgAdmin, organizationB, "Contract shared policy");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var policyId = (await response.Content.ReadFromJsonAsync<CreatePolicyResponse>())!.PolicyId;
        var policy = await orgAdmin.GetFromJsonAsync<PolicyResponse>(PolicyEndpoint(organizationB, policyId));
        Assert.Equal(new PolicyResponse(policyId, "Contract shared policy", organizationB), policy);
    }

    [Fact]
    public async Task DeleteOrganization_WithPolicies_KeepsOrganization()
    {
        var admin = await CreateAdminAsync("contract_organization_delete_blocked@example.com");
        var (organizationId, orgAdmin) = await CreateOrganizationWithOrgAdminAsync(factory, admin, "Contract blocked organization");
        await PostPolicyAsync(orgAdmin, organizationId, "Contract blocking policy");

        var response = await admin.DeleteAsync($"{OrganizationsEndpoint}/{organizationId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{OrganizationsEndpoint}/{organizationId}")).StatusCode);
    }

    [Fact]
    public async Task DeletePolicy_WithClearances_KeepsPolicy()
    {
        const string holderEmail = "contract_policy_holder@example.com";
        var admin = await CreateAdminAsync("contract_policy_delete_blocked@example.com");
        await TestUsers.CreateClientAsync(factory, holderEmail);
        var (organizationId, orgAdmin) = await CreateOrganizationWithOrgAdminAsync(factory, admin, "Contract cleared organization");
        var policyId = await CreatePolicyAsync(orgAdmin, "Contract cleared policy", organizationId);
        await CreateClearanceAsync(admin, await TestUsers.GetIdAsync(factory, holderEmail), policyId);

        var response = await orgAdmin.DeleteAsync(PolicyEndpoint(organizationId, policyId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await orgAdmin.GetAsync(PolicyEndpoint(organizationId, policyId))).StatusCode);
    }

    // The delete route's 403 is covered in ManageOrganizationsTests. A regular user on the policy routes gets
    // 404 Organization.NotFound since #95 step 3a (PolicyAccessTests)
    [Fact]
    public async Task CreateOrganization_RegularUser_Returns403()
    {
        var user = await TestUsers.CreateClientAsync(factory, "contract_regular@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await PostOrganizationAsync(user)).StatusCode);
    }

    [Fact]
    public async Task Commands_Anonymous_Return401()
    {
        var anonymous = factory.CreateClient();
        var someId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostOrganizationAsync(anonymous)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostPolicyAsync(anonymous, someId, "Anonymous policy")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"{OrganizationsEndpoint}/{someId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(PolicyEndpoint(someId, someId))).StatusCode);
    }

    // --- helpers ---

    private Task<HttpClient> CreateAdminAsync(string email) =>
        TestUsers.CreateClientAsync(factory, email, UserRoles.Admin);

    // Valid body, so only authorization can reject the request
    private static Task<HttpResponseMessage> PostOrganizationAsync(HttpClient client) =>
        client.PostAsJsonAsync(OrganizationsEndpoint, new { Name = "Unauthorized organization" });

    private record PolicyResponse(Guid Id, string Name, Guid OrganizationId);
    private record CreatePolicyResponse(Guid PolicyId);
}

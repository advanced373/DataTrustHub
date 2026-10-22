using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class ManagePoliciesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string OrganizationsEndpoint = "/admin/organizations";
    private const string PoliciesEndpoint = "/admin/policies";

    [Fact]
    public async Task ListPolicies_FilteredByOrganization_ReturnsOnlyThatOrganizationsPolicies()
    {
        var admin = await CreateAdminAsync("policies_list_admin@example.com");
        var organizationA = await CreateOrganizationAsync(admin, "Policies organization A");
        var organizationB = await CreateOrganizationAsync(admin, "Policies organization B");
        var policyA = await CreatePolicyAsync(admin, "Policy A", organizationA);
        var policyB = await CreatePolicyAsync(admin, "Policy B", organizationB);

        var all = await admin.GetFromJsonAsync<List<PolicyResponse>>(PoliciesEndpoint);
        var filtered = await admin.GetFromJsonAsync<List<PolicyResponse>>($"{PoliciesEndpoint}?organizationId={organizationA}");

        Assert.Contains(all!, p => p.Id == policyA);
        Assert.Contains(all!, p => p.Id == policyB);
        var only = Assert.Single(filtered!);
        Assert.Equal(policyA, only.Id);
    }

    [Fact]
    public async Task GetPolicy_ExistingAndMissing()
    {
        var admin = await CreateAdminAsync("policies_get_admin@example.com");
        var organizationId = await CreateOrganizationAsync(admin, "Policies get organization");
        var policyId = await CreatePolicyAsync(admin, "Readable policy", organizationId);

        var found = await admin.GetFromJsonAsync<PolicyResponse>($"{PoliciesEndpoint}/{policyId}");
        var missing = await admin.GetAsync($"{PoliciesEndpoint}/{Guid.NewGuid()}");

        Assert.Equal(new PolicyResponse(policyId, "Readable policy", organizationId), found);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task DeletePolicy_WithoutClearances_Returns204AndRemovesIt()
    {
        var admin = await CreateAdminAsync("policies_delete_admin@example.com");
        var organizationId = await CreateOrganizationAsync(admin, "Policies delete organization");
        var policyId = await CreatePolicyAsync(admin, "Disposable policy", organizationId);

        var response = await admin.DeleteAsync($"{PoliciesEndpoint}/{policyId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{PoliciesEndpoint}/{policyId}")).StatusCode);
    }

    [Fact]
    public async Task DeletePolicy_WithClearances_Returns409WithCode()
    {
        var admin = await CreateAdminAsync("policies_conflict_admin@example.com");
        var organizationId = await CreateOrganizationAsync(admin, "Policies conflict organization");
        var policyId = await CreatePolicyAsync(admin, "Policy with clearance", organizationId);
        await AddClearanceAsync(policyId);

        var response = await admin.DeleteAsync($"{PoliciesEndpoint}/{policyId}");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.PolicyHasClearances, AdminErrorMessages.PolicyHasClearances);
    }

    [Fact]
    public async Task DeletePolicy_Missing_Returns404WithCode()
    {
        var admin = await CreateAdminAsync("policies_delete_missing@example.com");

        var response = await admin.DeleteAsync($"{PoliciesEndpoint}/{Guid.NewGuid()}");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.PolicyNotFound, AdminErrorMessages.PolicyNotFound);
    }

    [Fact]
    public async Task PolicyEndpoints_RegularUser_Return403()
    {
        var user = await TestUsers.CreateClientAsync(factory, "policies_regular@example.com");
        var someId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(PoliciesEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"{PoliciesEndpoint}/{someId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"{PoliciesEndpoint}/{someId}")).StatusCode);
    }

    // --- helpers ---

    private Task<HttpClient> CreateAdminAsync(string email) =>
        TestUsers.CreateClientAsync(factory, email, UserRoles.Admin);

    private static async Task<Guid> CreateOrganizationAsync(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync(OrganizationsEndpoint, new { Name = name });
        return (await response.Content.ReadFromJsonAsync<CreateOrganizationResponse>())!.OrganizationId;
    }

    private static async Task<Guid> CreatePolicyAsync(HttpClient admin, string name, Guid organizationId)
    {
        var response = await admin.PostAsJsonAsync(PoliciesEndpoint, new { Name = name, OrganizationId = organizationId });
        return (await response.Content.ReadFromJsonAsync<CreatePolicyResponse>())!.PolicyId;
    }

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
    private record CreateOrganizationResponse(Guid OrganizationId);
    private record CreatePolicyResponse(Guid PolicyId);
}

using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class ManagePoliciesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string GroupsEndpoint = "/admin/groups";
    private const string PoliciesEndpoint = "/admin/policies";

    [Fact]
    public async Task ListPolicies_FilteredByOrganization_ReturnsOnlyThatOrganizationsPolicies()
    {
        var admin = await CreateAdminAsync("policies_list_admin@example.com");
        var groupA = await CreateGroupAsync(admin, "Policies group A");
        var groupB = await CreateGroupAsync(admin, "Policies group B");
        var policyA = await CreatePolicyAsync(admin, "Policy A", groupA);
        var policyB = await CreatePolicyAsync(admin, "Policy B", groupB);

        var all = await admin.GetFromJsonAsync<List<PolicyResponse>>(PoliciesEndpoint);
        var filtered = await admin.GetFromJsonAsync<List<PolicyResponse>>($"{PoliciesEndpoint}?organizationId={groupA}");

        Assert.Contains(all!, p => p.Id == policyA);
        Assert.Contains(all!, p => p.Id == policyB);
        var only = Assert.Single(filtered!);
        Assert.Equal(policyA, only.Id);
    }

    [Fact]
    public async Task GetPolicy_ExistingAndMissing()
    {
        var admin = await CreateAdminAsync("policies_get_admin@example.com");
        var groupId = await CreateGroupAsync(admin, "Policies get group");
        var policyId = await CreatePolicyAsync(admin, "Readable policy", groupId);

        var found = await admin.GetFromJsonAsync<PolicyResponse>($"{PoliciesEndpoint}/{policyId}");
        var missing = await admin.GetAsync($"{PoliciesEndpoint}/{Guid.NewGuid()}");

        Assert.Equal(new PolicyResponse(policyId, "Readable policy", groupId), found);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task DeletePolicy_WithoutClearances_Returns204AndRemovesIt()
    {
        var admin = await CreateAdminAsync("policies_delete_admin@example.com");
        var groupId = await CreateGroupAsync(admin, "Policies delete group");
        var policyId = await CreatePolicyAsync(admin, "Disposable policy", groupId);

        var response = await admin.DeleteAsync($"{PoliciesEndpoint}/{policyId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{PoliciesEndpoint}/{policyId}")).StatusCode);
    }

    [Fact]
    public async Task DeletePolicy_WithClearances_Returns409()
    {
        var admin = await CreateAdminAsync("policies_conflict_admin@example.com");
        var groupId = await CreateGroupAsync(admin, "Policies conflict group");
        var policyId = await CreatePolicyAsync(admin, "Policy with clearance", groupId);
        await AddClearanceAsync(policyId);

        var response = await admin.DeleteAsync($"{PoliciesEndpoint}/{policyId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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

    private static async Task<Guid> CreateGroupAsync(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync(GroupsEndpoint, new { Name = name });
        return (await response.Content.ReadFromJsonAsync<CreateGroupResponse>())!.GroupId;
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
    private record CreateGroupResponse(Guid GroupId);
    private record CreatePolicyResponse(Guid PolicyId);
}

using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;

namespace DataTrustHub.Features.Tests.Administration;

// ADR-0012 step 3b-1: group and policy command behaviour not covered by the per-slice tests
// (state kept on a conflict, per-group name uniqueness, authorization of all four routes)
public class GroupPolicyCommandContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreatePolicy_SameNameInOtherGroup_Returns200AndStoresPolicy()
    {
        var admin = await CreateAdminAsync("contract_policy_othergroup@example.com");
        var groupA = await CreateGroupAsync(admin, "Contract shared name group A");
        var groupB = await CreateGroupAsync(admin, "Contract shared name group B");
        await PostPolicyAsync(admin, "Contract shared policy", groupA);

        var response = await PostPolicyAsync(admin, "Contract shared policy", groupB);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var policyId = (await response.Content.ReadFromJsonAsync<CreatePolicyResponse>())!.PolicyId;
        var policy = await admin.GetFromJsonAsync<PolicyResponse>($"{PoliciesEndpoint}/{policyId}");
        Assert.Equal(new PolicyResponse(policyId, "Contract shared policy", groupB), policy);
    }

    [Fact]
    public async Task DeleteGroup_WithPolicies_KeepsGroup()
    {
        var admin = await CreateAdminAsync("contract_group_delete_blocked@example.com");
        var groupId = await CreateGroupAsync(admin, "Contract blocked group");
        await PostPolicyAsync(admin, "Contract blocking policy", groupId);

        var response = await admin.DeleteAsync($"{GroupsEndpoint}/{groupId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{GroupsEndpoint}/{groupId}")).StatusCode);
    }

    [Fact]
    public async Task DeletePolicy_WithClearances_KeepsPolicy()
    {
        const string holderEmail = "contract_policy_holder@example.com";
        var admin = await CreateAdminAsync("contract_policy_delete_blocked@example.com");
        await TestUsers.CreateClientAsync(factory, holderEmail);
        var policyId = await CreatePolicyAsync(admin, "Contract cleared policy");
        await CreateClearanceAsync(admin, await TestUsers.GetIdAsync(factory, holderEmail), policyId);

        var response = await admin.DeleteAsync($"{PoliciesEndpoint}/{policyId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{PoliciesEndpoint}/{policyId}")).StatusCode);
    }

    // The delete routes' 403 is covered in ManageGroupsTests and ManagePoliciesTests
    [Fact]
    public async Task CreateCommands_RegularUser_Return403()
    {
        var user = await TestUsers.CreateClientAsync(factory, "contract_regular@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await PostGroupAsync(user)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostPolicyAsync(user, "Forbidden policy", Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Commands_Anonymous_Return401()
    {
        var anonymous = factory.CreateClient();
        var someId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostGroupAsync(anonymous)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostPolicyAsync(anonymous, "Anonymous policy", someId)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"{GroupsEndpoint}/{someId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"{PoliciesEndpoint}/{someId}")).StatusCode);
    }

    // --- helpers ---

    private Task<HttpClient> CreateAdminAsync(string email) =>
        TestUsers.CreateClientAsync(factory, email, UserRoles.Admin);

    private static async Task<Guid> CreateGroupAsync(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync(GroupsEndpoint, new { Name = name });
        return (await response.Content.ReadFromJsonAsync<CreateGroupResponse>())!.GroupId;
    }

    // Valid body, so only authorization can reject the request
    private static Task<HttpResponseMessage> PostGroupAsync(HttpClient client) =>
        client.PostAsJsonAsync(GroupsEndpoint, new { Name = "Unauthorized group" });

    private static Task<HttpResponseMessage> PostPolicyAsync(HttpClient client, string name, Guid organizationId) =>
        client.PostAsJsonAsync(PoliciesEndpoint, new { Name = name, OrganizationId = organizationId });

    private record PolicyResponse(Guid Id, string Name, Guid OrganizationId);
    private record CreateGroupResponse(Guid GroupId);
    private record CreatePolicyResponse(Guid PolicyId);
}

using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class ManageGroupsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string GroupsEndpoint = "/admin/groups";
    private const string PoliciesEndpoint = "/admin/policies";

    [Fact]
    public async Task ListAndGetGroup_Admin_ReturnsCreatedGroup()
    {
        var admin = await CreateAdminAsync("groups_list_admin@example.com");
        var groupId = await CreateGroupAsync(admin, "Listed group");

        var groups = await admin.GetFromJsonAsync<List<GroupResponse>>(GroupsEndpoint);
        var group = await admin.GetFromJsonAsync<GroupResponse>($"{GroupsEndpoint}/{groupId}");

        Assert.Contains(groups!, g => g.Id == groupId && g.Name == "Listed group");
        Assert.Equal("Listed group", group!.Name);
    }

    [Fact]
    public async Task GetGroup_Missing_Returns404()
    {
        var admin = await CreateAdminAsync("groups_missing_admin@example.com");

        var response = await admin.GetAsync($"{GroupsEndpoint}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteGroup_WithoutPolicies_Returns204AndRemovesIt()
    {
        var admin = await CreateAdminAsync("groups_delete_admin@example.com");
        var groupId = await CreateGroupAsync(admin, "Disposable group");

        var response = await admin.DeleteAsync($"{GroupsEndpoint}/{groupId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{GroupsEndpoint}/{groupId}")).StatusCode);
    }

    [Fact]
    public async Task DeleteGroup_WithPolicies_Returns409WithCode()
    {
        var admin = await CreateAdminAsync("groups_conflict_admin@example.com");
        var groupId = await CreateGroupAsync(admin, "Group with policy");
        await admin.PostAsJsonAsync(PoliciesEndpoint, new { Name = "Blocking policy", OrganizationId = groupId });

        var response = await admin.DeleteAsync($"{GroupsEndpoint}/{groupId}");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.GroupHasPolicies, AdminErrorMessages.GroupHasPolicies);
    }

    [Fact]
    public async Task DeleteGroup_Missing_Returns404WithCode()
    {
        var admin = await CreateAdminAsync("groups_delete_missing@example.com");

        var response = await admin.DeleteAsync($"{GroupsEndpoint}/{Guid.NewGuid()}");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.GroupNotFound, AdminErrorMessages.GroupNotFound);
    }

    [Fact]
    public async Task GroupEndpoints_RegularUser_Return403()
    {
        var user = await TestUsers.CreateClientAsync(factory, "groups_regular@example.com");
        var someId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(GroupsEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"{GroupsEndpoint}/{someId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"{GroupsEndpoint}/{someId}")).StatusCode);
    }

    // --- helpers ---

    private Task<HttpClient> CreateAdminAsync(string email) =>
        TestUsers.CreateClientAsync(factory, email, UserRoles.Admin);

    private static async Task<Guid> CreateGroupAsync(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync(GroupsEndpoint, new { Name = name });
        return (await response.Content.ReadFromJsonAsync<CreateGroupResponse>())!.GroupId;
    }

    private record GroupResponse(Guid Id, string Name);
    private record CreateGroupResponse(Guid GroupId);
}

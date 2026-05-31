using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class CreatePolicyTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private const string CreateGroupEndpoint = "/admin/groups";
    private const string CreatePolicyEndpoint = "/admin/policies";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreatePolicy_WithValidData_Returns200WithPolicyId()
    {
        var groupId = await CreateGroupAndGetId("PolicyOrg1");

        var response = await _client.PostAsJsonAsync(
            CreatePolicyEndpoint,
            new { Name = "Confidential Access", OrganizationId = groupId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreatePolicyResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.PolicyId);
    }

    [Fact]
    public async Task CreatePolicy_WithDuplicateNameInSameOrg_Returns409()
    {
        var groupId = await CreateGroupAndGetId("PolicyOrg2");
        var body = new { Name = "Duplicate Policy", OrganizationId = groupId };
        await _client.PostAsJsonAsync(CreatePolicyEndpoint, body);

        var response = await _client.PostAsJsonAsync(CreatePolicyEndpoint, body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreatePolicy_WithEmptyName_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            CreatePolicyEndpoint,
            new { Name = "", OrganizationId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePolicy_WithNonExistentOrganization_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            CreatePolicyEndpoint,
            new { Name = "Some Policy", OrganizationId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> CreateGroupAndGetId(string name)
    {
        var response = await _client.PostAsJsonAsync(CreateGroupEndpoint, new { Name = name });
        var result = await response.Content.ReadFromJsonAsync<CreateGroupResponse>();
        return result!.GroupId;
    }

    private record CreateGroupResponse(Guid GroupId);
    private record CreatePolicyResponse(Guid PolicyId);
}

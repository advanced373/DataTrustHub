using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class CreatePolicyTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private const string CreateOrganizationEndpoint = "/admin/organizations";
    private const string CreatePolicyEndpoint = "/admin/policies";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreatePolicy_WithValidData_Returns200WithPolicyId()
    {
        var organizationId = await CreateOrganizationAndGetId("PolicyOrg1");

        var response = await _client.PostAsJsonAsync(
            CreatePolicyEndpoint,
            new { Name = "Confidential Access", OrganizationId = organizationId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreatePolicyResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.PolicyId);
    }

    [Fact]
    public async Task CreatePolicy_WithDuplicateNameInSameOrg_Returns409WithCode()
    {
        var organizationId = await CreateOrganizationAndGetId("PolicyOrg2");
        var body = new { Name = "Duplicate Policy", OrganizationId = organizationId };
        await _client.PostAsJsonAsync(CreatePolicyEndpoint, body);

        var response = await _client.PostAsJsonAsync(CreatePolicyEndpoint, body);

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.PolicyNameAlreadyInUse, AdminErrorMessages.PolicyNameAlreadyInUse);
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
    public async Task CreatePolicy_WithNonExistentOrganization_Returns404WithCode()
    {
        var response = await _client.PostAsJsonAsync(
            CreatePolicyEndpoint,
            new { Name = "Some Policy", OrganizationId = Guid.NewGuid() });

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.PolicyOrganizationNotFound, AdminErrorMessages.PolicyOrganizationNotFound);
    }

    private async Task<Guid> CreateOrganizationAndGetId(string name)
    {
        var response = await _client.PostAsJsonAsync(CreateOrganizationEndpoint, new { Name = name });
        var result = await response.Content.ReadFromJsonAsync<CreateOrganizationResponse>();
        return result!.OrganizationId;
    }

    private record CreateOrganizationResponse(Guid OrganizationId);
    private record CreatePolicyResponse(Guid PolicyId);
}

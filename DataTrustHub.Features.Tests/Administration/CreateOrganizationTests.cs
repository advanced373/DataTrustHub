using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class CreateOrganizationTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private const string CreateOrganizationEndpoint = "/admin/organizations";
    private const string OrganizationIdProperty = "organizationId";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateOrganization_WithValidData_Returns200WithOrganizationId()
    {
        var response = await _client.PostAsJsonAsync(
            CreateOrganizationEndpoint,
            new { Name = "Research Team" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateOrganizationResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.OrganizationId);
    }

    [Fact]
    public async Task CreateOrganization_WithValidData_ResponseHasOnlyOrganizationIdField()
    {
        var response = await _client.PostAsJsonAsync(
            CreateOrganizationEndpoint,
            new { Name = "Response Shape Team" });

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal([OrganizationIdProperty], json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.NotEqual(Guid.Empty, json.RootElement.GetProperty(OrganizationIdProperty).GetGuid());
    }

    [Fact]
    public async Task CreateOrganization_WithDuplicateName_Returns409WithCode()
    {
        var body = new { Name = "Duplicate Organization" };
        await _client.PostAsJsonAsync(CreateOrganizationEndpoint, body);

        var response = await _client.PostAsJsonAsync(CreateOrganizationEndpoint, body);

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.OrganizationNameAlreadyInUse, AdminErrorMessages.OrganizationNameAlreadyInUse);
    }

    [Fact]
    public async Task CreateOrganization_WithEmptyName_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            CreateOrganizationEndpoint,
            new { Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private record CreateOrganizationResponse(Guid OrganizationId);
}

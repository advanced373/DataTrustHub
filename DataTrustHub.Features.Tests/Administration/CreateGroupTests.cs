using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class CreateGroupTests(AdminApiFactory factory) : IClassFixture<AdminApiFactory>
{
    private const string CreateGroupEndpoint = "/admin/groups";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateGroup_WithValidData_Returns200WithGroupId()
    {
        var response = await _client.PostAsJsonAsync(
            CreateGroupEndpoint,
            new { Name = "Research Team" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateGroupResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.GroupId);
    }

    [Fact]
    public async Task CreateGroup_WithDuplicateName_Returns409()
    {
        var body = new { Name = "Duplicate Group" };
        await _client.PostAsJsonAsync(CreateGroupEndpoint, body);

        var response = await _client.PostAsJsonAsync(CreateGroupEndpoint, body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateGroup_WithEmptyName_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            CreateGroupEndpoint,
            new { Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private record CreateGroupResponse(Guid GroupId);
}

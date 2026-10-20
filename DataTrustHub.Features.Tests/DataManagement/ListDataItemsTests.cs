using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.DataManagement;

public class ListDataItemsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string UploadEndpoint = "/data/upload";
    private const string ListEndpoint = "/data";
    private const string Password = "Password1!";

    [Fact]
    public async Task ListDataItems_ReturnsOnlyOwnNonDeletedItems_WithoutContent()
    {
        var owner = await CreateAuthenticatedClientAsync("list_owner@example.com");
        var keptId = await UploadFileAsync(owner, "kept.txt", "keep me");
        var deletedId = await UploadFileAsync(owner, "deleted.txt", "delete me");
        await owner.DeleteAsync($"/data/{deletedId}");

        var other = await CreateAuthenticatedClientAsync("list_other@example.com");
        await UploadFileAsync(other, "foreign.txt", "not yours");

        var response = await owner.GetAsync(ListEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var items = await response.Content.ReadFromJsonAsync<List<DataItemSummary>>();
        var item = Assert.Single(items!);
        Assert.Equal(keptId, item.Id);
        Assert.Equal("kept.txt", item.Name);
        Assert.DoesNotContain("content", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListDataItems_NoItems_ReturnsEmptyList()
    {
        var client = await CreateAuthenticatedClientAsync("list_empty@example.com");

        var items = await client.GetFromJsonAsync<List<DataItemSummary>>(ListEndpoint);

        Assert.Empty(items!);
    }

    [Fact]
    public async Task ListDataItems_UnauthenticatedRequest_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(ListEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- helpers ---

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password });
        var login = await client.PostAsJsonAsync(LoginEndpoint, new { Email = email, Password });
        var token = (await login.Content.ReadFromJsonAsync<LoginUserResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> UploadFileAsync(HttpClient client, string fileName, string content)
    {
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent { { file, "file", fileName } };

        var response = await client.PostAsync(UploadEndpoint, form);
        return (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
    }

    private record DataItemSummary(Guid Id, string Name, long Size, string SecurityMarking);
    private record AddDataItemResponse(Guid DataItemId);
    private record LoginUserResponse(string Token);
}

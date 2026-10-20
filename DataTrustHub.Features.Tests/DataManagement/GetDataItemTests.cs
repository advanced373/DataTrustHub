using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.DataManagement;

public class GetDataItemTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string UploadEndpoint = "/data/upload";
    private const string Password = "Password1!";

    [Fact]
    public async Task GetDataItem_Owner_ReturnsItemWithContent()
    {
        var owner = await CreateAuthenticatedClientAsync("get_owner@example.com");
        var itemId = await UploadFileAsync(owner, "report.txt", "hello");

        var response = await owner.GetAsync($"/data/{itemId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<GetDataItemResponse>();
        Assert.Equal(itemId, item!.Id);
        Assert.Equal("report.txt", item.Name);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(item.Content!)));
    }

    [Fact]
    public async Task GetDataItem_NonOwner_Returns404()
    {
        var owner = await CreateAuthenticatedClientAsync("get_owner2@example.com");
        var itemId = await UploadFileAsync(owner, "secret.txt", "top secret");
        var intruder = await CreateAuthenticatedClientAsync("get_intruder@example.com");

        var response = await intruder.GetAsync($"/data/{itemId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDataItem_DeletedItem_Returns404()
    {
        var owner = await CreateAuthenticatedClientAsync("get_deleted@example.com");
        var itemId = await UploadFileAsync(owner, "gone.txt", "bye");
        await owner.DeleteAsync($"/data/{itemId}");

        var response = await owner.GetAsync($"/data/{itemId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDataItem_UnauthenticatedRequest_Returns401()
    {
        var response = await factory.CreateClient().GetAsync($"/data/{Guid.NewGuid()}");

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

    private record GetDataItemResponse(Guid Id, string Name, long Size, string SecurityMarking, string? Content);
    private record AddDataItemResponse(Guid DataItemId);
    private record LoginUserResponse(string Token);
}

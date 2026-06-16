using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.DataManagement;

public class DeleteDataItemTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string UploadEndpoint = "/data/upload";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task DeleteDataItem_Owner_ExistingItem_Returns204()
    {
        var token = await RegisterAndLoginAsync(_client, "delete_owner@example.com", "Password1!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var itemId = await UploadFileAsync(_client, "owner.txt", "hello");

        var response = await _client.DeleteAsync($"/data/{itemId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDataItem_UnauthenticatedRequest_Returns401()
    {
        var unauthClient = factory.CreateClient();

        var response = await unauthClient.DeleteAsync($"/data/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDataItem_NonExistentItem_Returns404()
    {
        var token = await RegisterAndLoginAsync(_client, "delete_missing@example.com", "Password1!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.DeleteAsync($"/data/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDataItem_NonOwner_Returns404()
    {
        var ownerClient = factory.CreateClient();
        var ownerToken = await RegisterAndLoginAsync(ownerClient, "delete_owner2@example.com", "Password1!");
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var itemId = await UploadFileAsync(ownerClient, "secret.txt", "top secret");

        var intruderClient = factory.CreateClient();
        var intruderToken = await RegisterAndLoginAsync(intruderClient, "delete_intruder@example.com", "Password1!");
        intruderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", intruderToken);

        var response = await intruderClient.DeleteAsync($"/data/{itemId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDataItem_AlreadyDeleted_Returns404()
    {
        var token = await RegisterAndLoginAsync(_client, "delete_twice@example.com", "Password1!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var itemId = await UploadFileAsync(_client, "ephemeral.txt", "gone soon");

        var firstDelete = await _client.DeleteAsync($"/data/{itemId}");
        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);

        var secondDelete = await _client.DeleteAsync($"/data/{itemId}");

        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    // --- helpers ---

    private static async Task<string> RegisterAndLoginAsync(HttpClient client, string email, string password)
    {
        await client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password = password });

        var loginResponse = await client.PostAsJsonAsync(
            LoginEndpoint, new { Email = email, Password = password });

        var login = await loginResponse.Content.ReadFromJsonAsync<LoginUserResponse>();
        return login!.Token;
    }

    private static async Task<Guid> UploadFileAsync(HttpClient client, string fileName, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var fileContentStream = new ByteArrayContent(bytes);
        fileContentStream.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var form = new MultipartFormDataContent();
        form.Add(fileContentStream, "file", fileName);

        var response = await client.PostAsync(UploadEndpoint, form);
        var result = await response.Content.ReadFromJsonAsync<AddDataItemResponse>();
        return result!.DataItemId;
    }

    private record AddDataItemResponse(Guid DataItemId);
    private record LoginUserResponse(string Token);
}

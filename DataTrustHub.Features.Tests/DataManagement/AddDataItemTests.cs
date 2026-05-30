using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.DataManagement;

public class AddDataItemTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string UploadEndpoint = "/data/upload";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task UploadFile_AuthenticatedUser_ValidFile_Returns200WithItemId()
    {
        var token = await RegisterAndLoginAsync("upload_valid@example.com", "Password1!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var content = BuildMultipartContent("hello world", "test.txt", "text/plain");
        var response = await _client.PostAsync(UploadEndpoint, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AddDataItemResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.DataItemId);
    }

    [Fact]
    public async Task UploadFile_UnauthenticatedRequest_Returns401()
    {
        // No Authorization header set — use a fresh client with no auth
        var unauthClient = factory.CreateClient();
        var content = BuildMultipartContent("some data", "test.txt", "text/plain");

        var response = await unauthClient.PostAsync(UploadEndpoint, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadFile_EmptyFile_Returns400()
    {
        var token = await RegisterAndLoginAsync("upload_empty@example.com", "Password1!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Send a multipart form with an empty file
        var content = BuildMultipartContent(string.Empty, "empty.txt", "text/plain");
        var response = await _client.PostAsync(UploadEndpoint, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadFile_MissingFilePart_Returns400()
    {
        var token = await RegisterAndLoginAsync("upload_missing@example.com", "Password1!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Send a multipart form with no file field at all
        var content = new MultipartFormDataContent();
        var response = await _client.PostAsync(UploadEndpoint, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- helpers ---

    private async Task<string> RegisterAndLoginAsync(string email, string password)
    {
        await _client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password = password });

        var loginResponse = await _client.PostAsJsonAsync(
            LoginEndpoint, new { Email = email, Password = password });

        var login = await loginResponse.Content.ReadFromJsonAsync<LoginUserResponse>();
        return login!.Token;
    }

    private static MultipartFormDataContent BuildMultipartContent(
        string fileContent, string fileName, string mediaType)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(fileContent);
        var fileContentStream = new ByteArrayContent(bytes);
        fileContentStream.Headers.ContentType = new MediaTypeHeaderValue(mediaType);

        var form = new MultipartFormDataContent();
        form.Add(fileContentStream, "file", fileName);
        return form;
    }

    private record AddDataItemResponse(Guid DataItemId);
    private record LoginUserResponse(string Token);
}

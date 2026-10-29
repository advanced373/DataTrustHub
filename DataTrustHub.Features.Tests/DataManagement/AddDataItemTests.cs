using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.DataManagement;

public class AddDataItemTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string UploadEndpoint = "/data/upload";
    private const string PersonalMarking = "UNCLASSIFIED";
    private const string UserNotFoundCode = "DataItem.UserNotFound";
    private const string UserNotFoundMessage = "The authenticated user could not be found.";

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

    [Fact]
    public async Task UploadFile_PersonalUpload_StoresUnclassifiedRowWithBase64Content()
    {
        const string fileText = "personal payload";
        const string fileName = "personal.txt";
        var client = await TestUsers.CreateClientAsync(factory, "upload_personal_row@example.com");
        var bytes = System.Text.Encoding.UTF8.GetBytes(fileText);

        var response = await client.PostAsync(UploadEndpoint, BuildMultipartContent(fileText, fileName, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var itemId = (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
        using var scope = factory.Services.CreateScope();
        var item = await scope.ServiceProvider.GetRequiredService<DContext>().DataItems.SingleAsync(d => d.Id == itemId);
        Assert.Equal(Convert.ToBase64String(bytes), item.Content);
        Assert.Equal((fileName, (long)bytes.Length, PersonalMarking), (item.Name, item.Size, item.SecurityMarking));
        Assert.Null(item.PolicyId);
        Assert.Null(item.ClassificationPriority);
        Assert.False(item.IsDeleted);
    }

    [Fact]
    public async Task UploadFile_OwnerRemovedAfterLogin_Returns404UserNotFound()
    {
        const string email = "upload_ghost@example.com";
        var client = await TestUsers.CreateClientAsync(factory, email);
        await RemoveUserAsync(email);

        var response = await client.PostAsync(UploadEndpoint, BuildMultipartContent("data", "ghost.txt", "text/plain"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal((UserNotFoundCode, UserNotFoundMessage), (error!.Code, error.Description));
    }

    // --- helpers ---

    private async Task RemoveUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        db.Users.Remove(await db.Users.SingleAsync(u => u.Email == email));
        await db.SaveChangesAsync();
    }

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
    private record ErrorResponse(string Code, string Description);
}

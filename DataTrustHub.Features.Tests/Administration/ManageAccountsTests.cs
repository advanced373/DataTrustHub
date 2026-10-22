using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

public class ManageAccountsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AccountsEndpoint = "/admin/accounts";
    private const string UploadEndpoint = "/data/upload";
    private static readonly object NewAccountBody = new { Email = "accounts_forbidden_new@example.com", Password = "Password1!" };

    [Fact]
    public async Task ListAccounts_Admin_ReturnsAccountsWithoutPasswordHashes()
    {
        var admin = await TestUsers.CreateClientAsync(factory, "accounts_list_admin@example.com", UserRoles.Admin);
        await TestUsers.CreateClientAsync(factory, "accounts_list_user@example.com");

        var response = await admin.GetAsync(AccountsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var accounts = await response.Content.ReadFromJsonAsync<List<AccountResponse>>();
        Assert.Contains(accounts!, a => a.Email == "accounts_list_user@example.com" && a.Role == UserRoles.User);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAccount_Admin_ExistingAndMissing()
    {
        var admin = await TestUsers.CreateClientAsync(factory, "accounts_get_admin@example.com", UserRoles.Admin);
        await TestUsers.CreateClientAsync(factory, "accounts_get_user@example.com");
        var userId = await TestUsers.GetIdAsync(factory, "accounts_get_user@example.com");

        var found = await admin.GetFromJsonAsync<AccountResponse>($"{AccountsEndpoint}/{userId}");
        var missing = await admin.GetAsync($"{AccountsEndpoint}/{Guid.NewGuid()}");

        Assert.Equal("accounts_get_user@example.com", found!.Email);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_Admin_RemovesUserAndClearances_SoftDeletesData()
    {
        var admin = await TestUsers.CreateClientAsync(factory, "accounts_delete_admin@example.com", UserRoles.Admin);
        var user = await TestUsers.CreateClientAsync(factory, "accounts_delete_user@example.com");
        var userId = await TestUsers.GetIdAsync(factory, "accounts_delete_user@example.com");
        var itemId = await UploadFileAsync(user);
        await AddClearanceAsync(userId);

        var response = await admin.DeleteAsync($"{AccountsEndpoint}/{userId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Id == userId));
        Assert.False(await db.Clearances.AnyAsync(c => c.UserId == userId));
        Assert.True((await db.DataItems.SingleAsync(d => d.Id == itemId)).IsDeleted);
    }

    [Fact]
    public async Task DeleteAccount_OwnAccount_Returns409()
    {
        var admin = await TestUsers.CreateClientAsync(factory, "accounts_self_admin@example.com", UserRoles.Admin);
        var adminId = await TestUsers.GetIdAsync(factory, "accounts_self_admin@example.com");

        var response = await admin.DeleteAsync($"{AccountsEndpoint}/{adminId}");

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict,
            AdminErrorCodes.AccountCannotDeleteOwnAccount, AdminErrorMessages.AccountCannotDeleteOwnAccount);
        using var scope = factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<DContext>().Users.AnyAsync(u => u.Id == adminId));
    }

    [Fact]
    public async Task DeleteAccount_MissingAccount_Returns404()
    {
        var admin = await TestUsers.CreateClientAsync(factory, "accounts_missing_admin@example.com", UserRoles.Admin);

        var response = await admin.DeleteAsync($"{AccountsEndpoint}/{Guid.NewGuid()}");

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.AccountNotFound, AdminErrorMessages.AccountNotFound);
    }

    [Fact]
    public async Task AccountEndpoints_RegularUser_Return403()
    {
        var user = await TestUsers.CreateClientAsync(factory, "accounts_regular@example.com");
        var someId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(AccountsEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"{AccountsEndpoint}/{someId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"{AccountsEndpoint}/{someId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync(AccountsEndpoint, NewAccountBody)).StatusCode);
    }

    [Fact]
    public async Task AccountEndpoints_Anonymous_Return401()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(AccountsEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"{AccountsEndpoint}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(AccountsEndpoint, NewAccountBody)).StatusCode);
    }

    // --- helpers ---

    private static async Task<Guid> UploadFileAsync(HttpClient client)
    {
        var file = new ByteArrayContent("owned"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var form = new MultipartFormDataContent { { file, "file", "owned.txt" } };

        var response = await client.PostAsync(UploadEndpoint, form);
        return (await response.Content.ReadFromJsonAsync<AddDataItemResponse>())!.DataItemId;
    }

    private async Task AddClearanceAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        db.Clearances.Add(new DbClearance
        {
            Id = Guid.NewGuid(),
            Name = "Test clearance",
            PolicyId = Guid.NewGuid(),
            UserId = userId,
            ClassificationLevel = new DbClassificationLevel { Name = "Secret", Priority = 2 }
        });
        await db.SaveChangesAsync();
    }

    private record AccountResponse(Guid Id, string Email, string Role);
    private record AddDataItemResponse(Guid DataItemId);
}

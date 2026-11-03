using System.Net;
using DataTrustHub.Features.Tests.WebApp.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.WebApp.Helpers.WebAppPages;

namespace DataTrustHub.Features.Tests.WebApp;

// #172 AC3: the registration requests page is for the platform admin only, reachable from the admin navigation,
// and refuses everyone else the same way the accounts page does
public class RegistrationRequestsAccessTests : IClassFixture<StubbedApiWebAppFactory>
{
    private readonly StubbedApiWebAppFactory factory;
    private readonly HttpClient client;

    public RegistrationRequestsAccessTests(StubbedApiWebAppFactory factory)
    {
        this.factory = factory;
        factory.Api.Reset();
        factory.Api.Answer(HttpMethod.Get, ApiAccounts, HttpStatusCode.OK, "[]");
        factory.Api.Answer(HttpMethod.Get, ApiRegistrationRequests, HttpStatusCode.OK, "[]");
        client = CreateClient(factory);
    }

    [Fact]
    public async Task RegistrationRequestsPage_Admin_Returns200()
    {
        await SignInAsync(factory, client, AdminRole);

        var response = await client.GetAsync(RegistrationRequestsPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RegistrationRequestsPage_NonAdmin_RedirectsToAccessDenied()
    {
        await SignInAsync(factory, client, UserRole);

        var response = await client.GetAsync(RegistrationRequestsPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(AccessDeniedPath, response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task RegistrationRequestsPage_NonAdmin_BehavesLikeTheAccountsPage()
    {
        await SignInAsync(factory, client, UserRole);

        var accounts = await client.GetAsync(UsersPath);
        var requests = await client.GetAsync(RegistrationRequestsPath);

        Assert.Equal(accounts.StatusCode, requests.StatusCode);
        Assert.Equal(accounts.Headers.Location!.AbsolutePath, requests.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task RegistrationRequestsPage_NonAdmin_DoesNotCallTheApi()
    {
        await SignInAsync(factory, client, UserRole);

        await client.GetAsync(RegistrationRequestsPath);

        Assert.False(factory.Api.WasCalled(HttpMethod.Get, ApiRegistrationRequests));
    }

    [Fact]
    public async Task RegistrationRequestsPage_Anonymous_RedirectsToLogin()
    {
        var response = await client.GetAsync(RegistrationRequestsPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(LoginPath, response.Headers.Location!.AbsolutePath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApproveOrReject_NonAdmin_IsDeniedWithoutCallingTheApi(bool approve)
    {
        await SignInAsync(factory, client, UserRole);
        var id = Guid.NewGuid();
        var token = AntiforgeryTokenOf(await client.GetStringAsync(HomePath));

        var response = await client.PostAsync(approve ? ApprovePath(id) : RejectPath(id),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = "someone@datatrusthub.test",
                [AntiforgeryField] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(AccessDeniedPath, response.Headers.Location!.AbsolutePath);
        Assert.False(factory.Api.WasCalled(HttpMethod.Post, approve ? ApiApprove(id) : ApiReject(id)));
    }

    [Fact]
    public async Task AdminOverview_Admin_LinksToRegistrationRequests()
    {
        await SignInAsync(factory, client, AdminRole);

        var page = await GetPageAsync(client, AdminPath);

        Assert.Contains($"href=\"{RegistrationRequestsPath}\"", page);
    }

    // The admin navigation tabs are shared by the administration pages
    [Fact]
    public async Task AdminNavigation_OnAccountsPage_HasRegistrationRequestsEntry()
    {
        await SignInAsync(factory, client, AdminRole);

        var page = await GetPageAsync(client, UsersPath);

        Assert.Contains($"href=\"{RegistrationRequestsPath}\"", page);
        Assert.Contains(RegistrationSpec.NavEntry, page);
    }

    [Fact]
    public async Task AdminNavigation_OnRegistrationRequestsPage_MarksItsEntryActive()
    {
        await SignInAsync(factory, client, AdminRole);

        var page = await GetPageAsync(client, RegistrationRequestsPath);

        Assert.Matches($"class=\"nav-link active\"\\s+href=\"{RegistrationRequestsPath}\"", page);
    }
}

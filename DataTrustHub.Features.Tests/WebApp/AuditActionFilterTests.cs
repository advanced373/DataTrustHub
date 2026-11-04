using System.Net;
using DataTrustHub.Features.Tests.WebApp.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.WebApp.Helpers.WebAppPages;

namespace DataTrustHub.Features.Tests.WebApp;

// #172 AC6: the platform admin's audit action filter offers the two new tenant-level actions
public class AuditActionFilterTests : IClassFixture<StubbedApiWebAppFactory>
{
    private readonly StubbedApiWebAppFactory factory;
    private readonly HttpClient client;

    public AuditActionFilterTests(StubbedApiWebAppFactory factory)
    {
        this.factory = factory;
        factory.Api.Reset();
        factory.Api.Answer(HttpMethod.Get, ApiAccounts, HttpStatusCode.OK, "[]");
        factory.Api.Answer(HttpMethod.Get, ApiAuditRecords, HttpStatusCode.OK, "[]");
        client = CreateClient(factory);
    }

    [Theory]
    [InlineData(RegistrationSpec.ApproveAction)]
    [InlineData(RegistrationSpec.RejectAction)]
    public async Task AuditPage_Admin_ActionFilterOffersTheAction(string action)
    {
        await SignInAsync(factory, client, AdminRole);

        var page = await GetPageAsync(client, AuditPath);

        Assert.Contains($"<option value=\"{action}\">{action}</option>", page);
    }

    // The existing tenant-level action stays in the filter
    [Fact]
    public async Task AuditPage_Admin_ActionFilterStillOffersRegisterUser()
    {
        await SignInAsync(factory, client, AdminRole);

        var page = await GetPageAsync(client, AuditPath);

        Assert.Contains("<option value=\"RegisterUser\">RegisterUser</option>", page);
    }
}

using System.Net;
using DataTrustHub.Features.Tests.Auditing.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Auditing;

// AC6 of #122 through the API, with the audit log failing like a database that is down (it fails only after
// the data is arranged): each lookup answers exactly as it does while the audit log works
public class FindUserByEmailFailingAuditTests(FailingAuditApiFactory factory) : IClassFixture<FailingAuditApiFactory>
{
    private const string EmailDomain = "@example.com";
    private const string MalformedEmail = "not-an-email";
    private const string AuditStoreDown = "The audit store is down.";

    [Fact]
    public async Task FindUserByEmail_FoundWhileAuditWriteFails_AnswersLikeWithAWorkingAuditLog()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_failing_found");
        await CreateUserAsync(factory, "lookup_failing_found_target");

        await AssertSameAnswerWhileAuditFailsAsync(scenario.OrgAdmin, scenario.OrganizationId, $"lookup_failing_found_target{EmailDomain}");
    }

    [Fact]
    public async Task FindUserByEmail_UserNotFoundWhileAuditWriteFails_AnswersLikeWithAWorkingAuditLog()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_failing_unknown");

        await AssertSameAnswerWhileAuditFailsAsync(scenario.OrgAdmin, scenario.OrganizationId, $"lookup_failing_unknown_nobody{EmailDomain}");
    }

    [Fact]
    public async Task FindUserByEmail_NotOrgAdminWhileAuditWriteFails_AnswersLikeWithAWorkingAuditLog()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_failing_outsider");
        var (outsider, _) = await CreateUserAsync(factory, "lookup_failing_outsider_caller");

        await AssertSameAnswerWhileAuditFailsAsync(outsider, scenario.OrganizationId, $"lookup_failing_outsider_caller{EmailDomain}");
    }

    [Fact]
    public async Task FindUserByEmail_InvalidEmailWhileAuditWriteFails_AnswersLikeWithAWorkingAuditLog()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "lookup_failing_invalid");

        await AssertSameAnswerWhileAuditFailsAsync(scenario.OrgAdmin, scenario.OrganizationId, MalformedEmail);
    }

    // --- helpers ---

    // The same lookup once with a working audit log and once with a failing one: same status, same body
    private async Task AssertSameAnswerWhileAuditFailsAsync(HttpClient caller, Guid organizationId, string email)
    {
        var working = await SendAsync(caller, organizationId, email);

        factory.AuditLog.ThrowOnAppend = new InvalidOperationException(AuditStoreDown);
        (HttpStatusCode Status, string Body) failing;
        try
        {
            failing = await SendAsync(caller, organizationId, email);
        }
        finally
        {
            factory.AuditLog.ThrowOnAppend = null;
        }

        Assert.Equal(working, failing);
    }

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpClient caller, Guid organizationId, string email)
    {
        var response = await PostFindUserByEmailAsync(caller, organizationId, email);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}

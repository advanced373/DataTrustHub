using System.Net;
using DataTrustHub.Features.Tests.Auditing.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;
using static DataTrustHub.Features.Tests.Auditing.Helpers.DataAccessAuditSetup;

namespace DataTrustHub.Features.Tests.Auditing;

// AC4 of #24 step 2 through the API, with the audit log failing like a database that is down (it fails only
// after the data is arranged): a download fails and no content leaves (fail closed), while a denied read still
// answers exactly like a missing item, so a failing audit store does not reveal existence (ADR-0011)
public class FailClosedDownloadTests(FailingAuditApiFactory factory) : IClassFixture<FailingAuditApiFactory>
{
    private const string AuditStoreDown = "The audit store is down.";

    [Fact]
    public async Task Download_AuditWriteFails_ReturnsNoContent()
    {
        var (owner, _) = await CreateUserAsync(factory, "fail_closed_content");
        var itemId = await UploadAsync(owner);
        var failure = new InvalidOperationException(AuditStoreDown);

        var outcome = await WhileAuditFailsAsync(failure, () => SendAsync(owner, itemId));

        AssertFailedWithoutContent(outcome, failure);
    }

    [Fact]
    public async Task Download_AuditWorks_ReturnsTheContent()
    {
        var (owner, _) = await CreateUserAsync(factory, "fail_closed_control");
        var itemId = await UploadAsync(owner);

        var response = await owner.GetAsync(ItemEndpoint(itemId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Base64Content, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeniedRead_AuditWriteFails_AnswersExactlyLikeAMissingItem()
    {
        var scenario = await ArrangeClassifiedAsync(factory, "fail_closed_denied");
        var (intruder, _) = await CreateUserAsync(factory, "fail_closed_denied_intruder");

        var (denied, missing) = await WhileAuditFailsAsync(new InvalidOperationException(AuditStoreDown), async () =>
            (await SendAsync(intruder, scenario.ItemId), await SendAsync(intruder, Guid.NewGuid())));

        Assert.Null(denied.Exception);
        Assert.Equal((HttpStatusCode.NotFound, missing.Body), (denied.Status, denied.Body));
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
    }

    // --- helpers ---

    private async Task<T> WhileAuditFailsAsync<T>(Exception failure, Func<Task<T>> action)
    {
        factory.AuditLog.ThrowOnAppend = failure;
        try
        {
            return await action();
        }
        finally
        {
            factory.AuditLog.ThrowOnAppend = null;
        }
    }

    // The test server either rethrows the handler's exception to the client or answers 500; both are a failure
    private static async Task<RequestOutcome> SendAsync(HttpClient client, Guid itemId)
    {
        try
        {
            var response = await client.GetAsync(ItemEndpoint(itemId));
            return new RequestOutcome(response.StatusCode, await response.Content.ReadAsStringAsync(), null);
        }
        catch (Exception exception)
        {
            return new RequestOutcome(null, null, exception);
        }
    }

    private static void AssertFailedWithoutContent(RequestOutcome outcome, Exception failure)
    {
        if (outcome.Exception is not null)
        {
            Assert.Same(failure, outcome.Exception);
            return;
        }

        Assert.Equal(HttpStatusCode.InternalServerError, outcome.Status);
        Assert.DoesNotContain(Base64Content, outcome.Body!);
        Assert.DoesNotContain(FileContent, outcome.Body!);
    }

    private sealed record RequestOutcome(HttpStatusCode? Status, string? Body, Exception? Exception);
}

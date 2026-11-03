using System.Globalization;
using System.Net;
using DataTrustHub.Features.Tests.WebApp.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.WebApp.Helpers.WebAppPages;

namespace DataTrustHub.Features.Tests.WebApp;

// #172 AC4 and AC5: the platform admin's registration requests page lists the pending requests and approves
// or rejects them through the API
public class RegistrationRequestsPageTests : IClassFixture<StubbedApiWebAppFactory>
{
    private const string ListErrorDescription = "The registration requests could not be loaded.";

    private static readonly PendingRequest Oldest = new(
        Guid.NewGuid(), "oldest@datatrusthub.test",
        new DateTime(2026, 10, 30, 8, 15, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 8, 15, 0, DateTimeKind.Utc));

    private static readonly PendingRequest Newest = new(
        Guid.NewGuid(), "newest@datatrusthub.test",
        new DateTime(2026, 11, 2, 17, 45, 30, DateTimeKind.Utc), new DateTime(2026, 11, 9, 17, 45, 30, DateTimeKind.Utc));

    private readonly StubbedApiWebAppFactory factory;
    private readonly HttpClient client;

    public RegistrationRequestsPageTests(StubbedApiWebAppFactory factory)
    {
        this.factory = factory;
        factory.Api.Reset();
        client = CreateClient(factory);
    }

    // AC4
    [Fact]
    public async Task Index_WithRequests_ShowsEachEmail()
    {
        var page = await OpenPageAsync(RegistrationSpec.RequestsBody(Oldest, Newest));

        Assert.Contains(Oldest.Email, page);
        Assert.Contains(Newest.Email, page);
    }

    // AC4
    [Fact]
    public async Task Index_WithRequests_ShowsRequestedAtAndExpiresAtInUtc()
    {
        var page = await OpenPageAsync(RegistrationSpec.RequestsBody(Oldest));

        Assert.Contains(Format(Oldest.RequestedAt), page);
        Assert.Contains(Format(Oldest.ExpiresAt), page);
    }

    // AC4
    [Fact]
    public async Task Index_WithRequests_HasRequestedAtAndExpiresAtColumns()
    {
        var page = await OpenPageAsync(RegistrationSpec.RequestsBody(Oldest));

        Assert.Contains("Requested at", page);
        Assert.Contains("Expires at", page);
    }

    // AC4: the API returns the requests oldest first and the page keeps that order
    [Fact]
    public async Task Index_WithRequests_ListsOldestFirst()
    {
        var page = await OpenPageAsync(RegistrationSpec.RequestsBody(Oldest, Newest));

        Assert.True(page.IndexOf(Oldest.Email, StringComparison.Ordinal)
            < page.IndexOf(Newest.Email, StringComparison.Ordinal));
    }

    // AC4
    [Fact]
    public async Task Index_NoRequests_ShowsEmptyState()
    {
        var page = await OpenPageAsync("[]");

        Assert.Contains(RegistrationSpec.EmptyState, page);
    }

    // AC4
    [Fact]
    public async Task Index_ListFails_ShowsTheErrorAsListError()
    {
        var page = await OpenPageAsync(
            RegistrationSpec.ErrorBody("Unexpected", ListErrorDescription), HttpStatusCode.InternalServerError);

        Assert.Matches($"<div class=\"alert alert-danger\" role=\"alert\">\\s*{ListErrorDescription}", page);
    }

    // AC4
    [Fact]
    public async Task Index_ListFails_DoesNotShowEmptyState()
    {
        var page = await OpenPageAsync(
            RegistrationSpec.ErrorBody("Unexpected", ListErrorDescription), HttpStatusCode.InternalServerError);

        Assert.DoesNotContain(RegistrationSpec.EmptyState, page);
    }

    // AC4
    [Fact]
    public async Task Index_Admin_CallsTheListRoute()
    {
        await OpenPageAsync("[]");

        Assert.True(factory.Api.WasCalled(HttpMethod.Get, ApiRegistrationRequests));
    }

    // AC5: each row has an Approve and a Reject form posting to that request's routes
    [Fact]
    public async Task Index_WithRequest_RowHasApproveAndRejectPostForms()
    {
        var page = await OpenPageAsync(RegistrationSpec.RequestsBody(Oldest));

        Assert.Single(FormsOf(page), form => IsPostTo(form.Tag, ApprovePath(Oldest.Id)));
        Assert.Single(FormsOf(page), form => IsPostTo(form.Tag, RejectPath(Oldest.Id)));
    }

    // AC5
    [Fact]
    public async Task Index_WithRequest_ApproveAndRejectFormsCarryAntiforgeryToken()
    {
        var page = await OpenPageAsync(RegistrationSpec.RequestsBody(Oldest));

        var rowForms = FormsOf(page).Where(form =>
            IsPostTo(form.Tag, ApprovePath(Oldest.Id)) || IsPostTo(form.Tag, RejectPath(Oldest.Id))).ToList();
        Assert.Equal(2, rowForms.Count);
        Assert.All(rowForms, form => Assert.Contains($"name=\"{AntiforgeryField}\"", form.Inner));
    }

    // AC5: the success message names the email, which the row's forms post along with the token
    [Fact]
    public async Task Index_WithRequest_ApproveAndRejectFormsPostTheRequestsEmail()
    {
        var page = await OpenPageAsync(RegistrationSpec.RequestsBody(Oldest));

        var rowForms = FormsOf(page).Where(form =>
            IsPostTo(form.Tag, ApprovePath(Oldest.Id)) || IsPostTo(form.Tag, RejectPath(Oldest.Id))).ToList();
        Assert.Equal(2, rowForms.Count);
        Assert.All(rowForms, form => Assert.Contains($"name=\"email\" value=\"{Oldest.Email}\"", form.Inner));
    }

    // AC5: Reject asks for confirmation the same way the delete buttons do
    [Fact]
    public async Task Index_WithRequest_RejectFormAsksForConfirmation()
    {
        var page = await OpenPageAsync(RegistrationSpec.RequestsBody(Oldest));

        var reject = Assert.Single(FormsOf(page), form => IsPostTo(form.Tag, RejectPath(Oldest.Id)));
        Assert.Contains("onsubmit=\"return confirm(this.dataset.confirm);\"", reject.Tag);
        Assert.Matches("data-confirm=\"[^\"]+\"", reject.Tag);
    }

    // AC5: approving needs no confirmation
    [Fact]
    public async Task Index_WithRequest_ApproveFormDoesNotAskForConfirmation()
    {
        var page = await OpenPageAsync(RegistrationSpec.RequestsBody(Oldest));

        var approve = Assert.Single(FormsOf(page), form => IsPostTo(form.Tag, ApprovePath(Oldest.Id)));
        Assert.DoesNotContain("confirm(", approve.Tag);
    }

    // AC5
    [Fact]
    public async Task Approve_Success_CallsTheApproveRoute()
    {
        factory.Api.Answer(HttpMethod.Post, ApiApprove(Oldest.Id), HttpStatusCode.OK,
            RegistrationSpec.UserCreatedBody(Guid.NewGuid()));

        await PostRowActionAsync(ApprovePath(Oldest.Id));

        Assert.True(factory.Api.WasCalled(HttpMethod.Post, ApiApprove(Oldest.Id)));
        Assert.False(factory.Api.WasCalled(HttpMethod.Post, ApiReject(Oldest.Id)));
    }

    // AC5
    [Fact]
    public async Task Approve_Success_RedirectsBackWithSuccessMessage()
    {
        factory.Api.Answer(HttpMethod.Post, ApiApprove(Oldest.Id), HttpStatusCode.OK,
            RegistrationSpec.UserCreatedBody(Guid.NewGuid()));

        var response = await PostRowActionAsync(ApprovePath(Oldest.Id));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(RegistrationRequestsPath, response.Headers.Location!.OriginalString);
        var page = await FollowAsync(client, response);
        Assert.Contains(SuccessAlert(string.Format(RegistrationSpec.ApprovedFormat, Oldest.Email)), Collapse(page));
    }

    // AC5
    [Theory]
    [InlineData(HttpStatusCode.NotFound, RegistrationSpec.NotFoundCode, RegistrationSpec.NotFoundDescription)]
    [InlineData(HttpStatusCode.Conflict, RegistrationSpec.EmailInUseCode, RegistrationSpec.EmailInUseDescription)]
    public async Task Approve_Failure_RedirectsBackWithApiDescription(
        HttpStatusCode statusCode, string code, string description)
    {
        factory.Api.Answer(HttpMethod.Post, ApiApprove(Oldest.Id), statusCode, RegistrationSpec.ErrorBody(code, description));

        var response = await PostRowActionAsync(ApprovePath(Oldest.Id));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(RegistrationRequestsPath, response.Headers.Location!.OriginalString);
        var page = Collapse(await FollowAsync(client, response));
        Assert.Contains(description, page);
        Assert.DoesNotContain(string.Format(RegistrationSpec.ApprovedFormat, Oldest.Email), page);
    }

    // AC5: the description is shown as an alert, not as a success
    [Fact]
    public async Task Approve_Failure_DescriptionIsNotASuccessAlert()
    {
        factory.Api.Answer(HttpMethod.Post, ApiApprove(Oldest.Id), HttpStatusCode.Conflict,
            RegistrationSpec.ErrorBody(RegistrationSpec.EmailInUseCode, RegistrationSpec.EmailInUseDescription));

        var page = Collapse(await FollowAsync(client, await PostRowActionAsync(ApprovePath(Oldest.Id))));

        Assert.DoesNotContain(SuccessAlert(RegistrationSpec.EmailInUseDescription), page);
        Assert.Matches($"class=\"alert alert-(warning|danger)[^\"]*\" role=\"alert\"> {RegistrationSpec.EmailInUseDescription}", page);
    }

    // AC5
    [Fact]
    public async Task Reject_Success_CallsTheRejectRoute()
    {
        factory.Api.Answer(HttpMethod.Post, ApiReject(Oldest.Id), HttpStatusCode.NoContent);

        await PostRowActionAsync(RejectPath(Oldest.Id));

        Assert.True(factory.Api.WasCalled(HttpMethod.Post, ApiReject(Oldest.Id)));
        Assert.False(factory.Api.WasCalled(HttpMethod.Post, ApiApprove(Oldest.Id)));
    }

    // AC5
    [Fact]
    public async Task Reject_Success_RedirectsBackWithRejectedMessage()
    {
        factory.Api.Answer(HttpMethod.Post, ApiReject(Oldest.Id), HttpStatusCode.NoContent);

        var response = await PostRowActionAsync(RejectPath(Oldest.Id));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(RegistrationRequestsPath, response.Headers.Location!.OriginalString);
        var page = await FollowAsync(client, response);
        Assert.Contains(SuccessAlert(string.Format(RegistrationSpec.RejectedFormat, Oldest.Email)), Collapse(page));
    }

    // AC5
    [Fact]
    public async Task Reject_NotFound_RedirectsBackWithApiDescription()
    {
        factory.Api.Answer(HttpMethod.Post, ApiReject(Oldest.Id), HttpStatusCode.NotFound,
            RegistrationSpec.ErrorBody(RegistrationSpec.NotFoundCode, RegistrationSpec.NotFoundDescription));

        var response = await PostRowActionAsync(RejectPath(Oldest.Id));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var page = Collapse(await FollowAsync(client, response));
        Assert.Contains(RegistrationSpec.NotFoundDescription, page);
        Assert.DoesNotContain(string.Format(RegistrationSpec.RejectedFormat, Oldest.Email), page);
    }

    // AC5: the antiforgery token is required, like every other form post
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApproveOrReject_WithoutAntiforgeryToken_IsRejectedWithoutCallingTheApi(bool approve)
    {
        await SignInAsync(factory, client, AdminRole);
        var path = approve ? ApprovePath(Oldest.Id) : RejectPath(Oldest.Id);

        var response = await client.PostAsync(path,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = Oldest.Email }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(factory.Api.WasCalled(HttpMethod.Post, approve ? ApiApprove(Oldest.Id) : ApiReject(Oldest.Id)));
    }

    private async Task<string> OpenPageAsync(string listBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        factory.Api.Answer(HttpMethod.Get, ApiRegistrationRequests, statusCode, listBody);
        await SignInAsync(factory, client, AdminRole);
        return await GetPageAsync(client, RegistrationRequestsPath);
    }

    // Opens the page with the request listed, then posts the row's form with its hidden email and token
    private async Task<HttpResponseMessage> PostRowActionAsync(string path)
    {
        factory.Api.Answer(HttpMethod.Get, ApiRegistrationRequests, HttpStatusCode.OK, RegistrationSpec.RequestsBody(Oldest));
        await SignInAsync(factory, client, AdminRole);
        return await PostFormAsync(client, RegistrationRequestsPath, path,
            new Dictionary<string, string> { ["email"] = Oldest.Email });
    }

    private static bool IsPostTo(string formTag, string path) =>
        formTag.Contains($"action=\"{path}\"", StringComparison.OrdinalIgnoreCase)
        && formTag.Contains("method=\"post\"", StringComparison.OrdinalIgnoreCase);

    private static string Format(DateTime time) => time.ToString(RegistrationSpec.TimeFormat, CultureInfo.InvariantCulture);

    private static string SuccessAlert(string message) =>
        $"class=\"alert alert-success alert-dismissible fade show\" role=\"alert\"> {message}";

    // Collapses runs of whitespace so alert markup can be matched regardless of indentation
    private static string Collapse(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "\\s+", " ");
}

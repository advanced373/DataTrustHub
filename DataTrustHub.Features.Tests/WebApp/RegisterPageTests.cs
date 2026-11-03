using System.Net;
using DataTrustHub.Features.Tests.WebApp.Helpers;
using Microsoft.Net.Http.Headers;
using Xunit;
using static DataTrustHub.Features.Tests.WebApp.Helpers.WebAppPages;

namespace DataTrustHub.Features.Tests.WebApp;

// #172 AC1 and AC2: the register page after the API answers register with 202, 409, 503 or 429
public class RegisterPageTests : IClassFixture<StubbedApiWebAppFactory>
{
    private const string Email = "new.user@datatrusthub.test";
    private const string Password = "Secret-Pass-123";

    private readonly StubbedApiWebAppFactory factory;
    private readonly HttpClient client;

    public RegisterPageTests(StubbedApiWebAppFactory factory)
    {
        this.factory = factory;
        factory.Api.Reset();
        client = CreateClient(factory);
    }

    // AC1
    [Fact]
    public async Task Register_Accepted_RedirectsToLogin()
    {
        AnswerRegister(HttpStatusCode.Accepted, RegistrationSpec.RegistrationRequestCreatedBody(Guid.NewGuid()));

        var response = await RegisterAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(LoginPath, response.Headers.Location!.OriginalString);
    }

    // AC1
    [Fact]
    public async Task Register_Accepted_LoginPageShowsRequestSentMessage()
    {
        AnswerRegister(HttpStatusCode.Accepted, RegistrationSpec.RegistrationRequestCreatedBody(Guid.NewGuid()));

        var page = await FollowAsync(client, await RegisterAsync());

        Assert.Contains(RegistrationSpec.RequestSent, page);
    }

    // AC1
    [Fact]
    public async Task Register_Accepted_CallsRegisterRouteWithTheCredentials()
    {
        AnswerRegister(HttpStatusCode.Accepted, RegistrationSpec.RegistrationRequestCreatedBody(Guid.NewGuid()));

        await RegisterAsync();

        var request = Assert.Single(factory.Api.Requests, request => request.Path == ApiRegister);
        Assert.Equal(HttpMethod.Post.Method, request.Method);
        Assert.Contains(Email, request.Body);
    }

    // AC1: no automatic sign-in
    [Fact]
    public async Task Register_Accepted_SetsNoAuthenticationCookie()
    {
        AnswerRegister(HttpStatusCode.Accepted, RegistrationSpec.RegistrationRequestCreatedBody(Guid.NewGuid()));

        var response = await RegisterAsync();

        var cookies = response.Headers.TryGetValues(HeaderNames.SetCookie, out var values) ? values : [];
        Assert.DoesNotContain(cookies, cookie => cookie.StartsWith(AuthenticationCookie, StringComparison.Ordinal));
    }

    // AC1: no automatic sign-in
    [Fact]
    public async Task Register_Accepted_HomePageStillRedirectsToLogin()
    {
        AnswerRegister(HttpStatusCode.Accepted, RegistrationSpec.RegistrationRequestCreatedBody(Guid.NewGuid()));
        await FollowAsync(client, await RegisterAsync());

        var home = await client.GetAsync(HomePath);

        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.StartsWith(LoginPath, home.Headers.Location!.AbsolutePath);
    }

    // AC2
    [Theory]
    [InlineData(RegistrationSpec.EmailInUseCode, RegistrationSpec.EmailInUseDescription)]
    [InlineData(RegistrationSpec.AlreadyPendingCode, RegistrationSpec.AlreadyPendingDescription)]
    public async Task Register_Conflict_ShowsApiDescriptionOnTheForm(string code, string description)
    {
        AnswerRegister(HttpStatusCode.Conflict, RegistrationSpec.ErrorBody(code, description));

        var response = await RegisterAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(description, await DecodedBodyAsync(response));
    }

    // AC2
    [Fact]
    public async Task Register_QueueFull_ShowsApiDescriptionOnTheForm()
    {
        AnswerRegister(HttpStatusCode.ServiceUnavailable,
            RegistrationSpec.ErrorBody(RegistrationSpec.QueueFullCode, RegistrationSpec.QueueFullDescription));

        var response = await RegisterAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(RegistrationSpec.QueueFullDescription, await DecodedBodyAsync(response));
    }

    // AC2: the rate limiter answers 429 with an empty body
    [Fact]
    public async Task Register_TooManyRequests_ShowsTooManyAttemptsMessage()
    {
        AnswerRegister(HttpStatusCode.TooManyRequests);

        var response = await RegisterAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(RegistrationSpec.TooManyRegistrationAttempts, await DecodedBodyAsync(response));
    }

    // AC2
    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Register_Failure_FormKeepsTheEmail(HttpStatusCode statusCode)
    {
        AnswerFailure(statusCode);

        var page = await DecodedBodyAsync(await RegisterAsync());

        Assert.Contains($"value=\"{Email}\"", page);
    }

    // AC2
    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Register_Failure_FormDoesNotEchoThePassword(HttpStatusCode statusCode)
    {
        AnswerFailure(statusCode);

        var page = await DecodedBodyAsync(await RegisterAsync());

        Assert.DoesNotContain(Password, page);
    }

    private void AnswerFailure(HttpStatusCode statusCode)
    {
        var body = statusCode == HttpStatusCode.TooManyRequests
            ? null
            : RegistrationSpec.ErrorBody(RegistrationSpec.EmailInUseCode, RegistrationSpec.EmailInUseDescription);
        AnswerRegister(statusCode, body);
    }

    private void AnswerRegister(HttpStatusCode statusCode, string? body = null) =>
        factory.Api.Answer(HttpMethod.Post, ApiRegister, statusCode, body);

    private Task<HttpResponseMessage> RegisterAsync() =>
        PostFormAsync(client, RegisterPath, RegisterPath, new Dictionary<string, string>
        {
            ["Email"] = Email,
            ["Password"] = Password,
            ["ConfirmPassword"] = Password
        });

    private static async Task<string> DecodedBodyAsync(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
}

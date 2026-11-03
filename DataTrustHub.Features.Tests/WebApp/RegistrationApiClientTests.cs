extern alias WebApp;

using System.Net;
using DataTrustHub.Features.Tests.WebApp.Helpers;
using Microsoft.Extensions.DependencyInjection;
using WebApp::DataTrustHub.Services.Api;
using Xunit;
using static DataTrustHub.Features.Tests.WebApp.Helpers.WebAppPages;

namespace DataTrustHub.Features.Tests.WebApp;

// #172 AC1, AC2, AC4 and AC5 at the API clients: the WebApp's real typed clients, resolved from its container,
// read the API's 202/200/204 answers and turn 404/409/429/503 into results
public class RegistrationApiClientTests : IClassFixture<StubbedApiWebAppFactory>, IDisposable
{
    private const string Email = "client.test@datatrusthub.test";
    private const string Password = "Secret-Pass-123";

    private readonly StubbedApiWebAppFactory factory;
    private readonly IServiceScope scope;
    private readonly IAuthApiClient authApi;
    private readonly IAdminApiClient adminApi;

    public RegistrationApiClientTests(StubbedApiWebAppFactory factory)
    {
        this.factory = factory;
        factory.Api.Reset();
        scope = factory.Services.CreateScope();
        authApi = scope.ServiceProvider.GetRequiredService<IAuthApiClient>();
        adminApi = scope.ServiceProvider.GetRequiredService<IAdminApiClient>();
    }

    public void Dispose() => scope.Dispose();

    // AC1
    [Fact]
    public async Task Register_Accepted_ReturnsTheRegistrationRequestId()
    {
        var id = Guid.NewGuid();
        factory.Api.Answer(HttpMethod.Post, ApiRegister, HttpStatusCode.Accepted, RegistrationSpec.RegistrationRequestCreatedBody(id));

        var result = await authApi.RegisterAsync(Email, Password, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(id, result.Value);
    }

    // AC1
    [Fact]
    public async Task Register_Accepted_PostsToTheRegisterRoute()
    {
        factory.Api.Answer(HttpMethod.Post, ApiRegister, HttpStatusCode.Accepted,
            RegistrationSpec.RegistrationRequestCreatedBody(Guid.NewGuid()));

        await authApi.RegisterAsync(Email, Password, CancellationToken.None);

        Assert.True(factory.Api.WasCalled(HttpMethod.Post, ApiRegister));
    }

    // AC2
    [Theory]
    [InlineData(HttpStatusCode.Conflict, RegistrationSpec.EmailInUseCode, RegistrationSpec.EmailInUseDescription)]
    [InlineData(HttpStatusCode.Conflict, RegistrationSpec.AlreadyPendingCode, RegistrationSpec.AlreadyPendingDescription)]
    [InlineData(HttpStatusCode.ServiceUnavailable, RegistrationSpec.QueueFullCode, RegistrationSpec.QueueFullDescription)]
    public async Task Register_Failure_ReturnsTheApiDescription(HttpStatusCode statusCode, string code, string description)
    {
        factory.Api.Answer(HttpMethod.Post, ApiRegister, statusCode, RegistrationSpec.ErrorBody(code, description));

        var result = await authApi.RegisterAsync(Email, Password, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(description, result.ErrorMessage);
    }

    // AC2
    [Fact]
    public async Task Register_TooManyRequests_ReturnsTooManyRegistrationAttempts()
    {
        factory.Api.Answer(HttpMethod.Post, ApiRegister, HttpStatusCode.TooManyRequests);

        var result = await authApi.RegisterAsync(Email, Password, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsUnauthorized);
        Assert.Equal(RegistrationSpec.TooManyRegistrationAttempts, result.ErrorMessage);
    }

    // AC4
    [Fact]
    public async Task ListRegistrationRequests_Ok_ReadsEveryFieldInOrder()
    {
        var oldest = new PendingRequest(Guid.NewGuid(), "a@datatrusthub.test",
            new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc));
        var newest = new PendingRequest(Guid.NewGuid(), "b@datatrusthub.test",
            new DateTime(2026, 11, 1, 9, 30, 0, DateTimeKind.Utc), new DateTime(2026, 11, 8, 9, 30, 0, DateTimeKind.Utc));
        factory.Api.Answer(HttpMethod.Get, ApiRegistrationRequests, HttpStatusCode.OK, RegistrationSpec.RequestsBody(oldest, newest));

        var result = await adminApi.ListRegistrationRequestsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(result.Value!,
            first => AssertRequest(oldest, first),
            second => AssertRequest(newest, second));
    }

    // AC4
    [Fact]
    public async Task ListRegistrationRequests_Error_ReturnsTheApiDescription()
    {
        factory.Api.Answer(HttpMethod.Get, ApiRegistrationRequests, HttpStatusCode.InternalServerError,
            RegistrationSpec.ErrorBody("Unexpected", "Listing failed."));

        var result = await adminApi.ListRegistrationRequestsAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Listing failed.", result.ErrorMessage);
    }

    // AC5
    [Fact]
    public async Task ApproveRegistrationRequest_Ok_ReturnsTheNewUserId()
    {
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        factory.Api.Answer(HttpMethod.Post, ApiApprove(requestId), HttpStatusCode.OK, RegistrationSpec.UserCreatedBody(userId));

        var result = await adminApi.ApproveRegistrationRequestAsync(requestId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value);
    }

    // AC5
    [Theory]
    [InlineData(HttpStatusCode.NotFound, RegistrationSpec.NotFoundCode, RegistrationSpec.NotFoundDescription)]
    [InlineData(HttpStatusCode.Conflict, RegistrationSpec.EmailInUseCode, RegistrationSpec.EmailInUseDescription)]
    public async Task ApproveRegistrationRequest_Failure_ReturnsTheApiDescription(
        HttpStatusCode statusCode, string code, string description)
    {
        var requestId = Guid.NewGuid();
        factory.Api.Answer(HttpMethod.Post, ApiApprove(requestId), statusCode, RegistrationSpec.ErrorBody(code, description));

        var result = await adminApi.ApproveRegistrationRequestAsync(requestId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(description, result.ErrorMessage);
    }

    // AC5
    [Fact]
    public async Task RejectRegistrationRequest_NoContent_Succeeds()
    {
        var requestId = Guid.NewGuid();
        factory.Api.Answer(HttpMethod.Post, ApiReject(requestId), HttpStatusCode.NoContent);

        var result = await adminApi.RejectRegistrationRequestAsync(requestId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(factory.Api.WasCalled(HttpMethod.Post, ApiReject(requestId)));
    }

    // AC5
    [Fact]
    public async Task RejectRegistrationRequest_NotFound_ReturnsTheApiDescription()
    {
        var requestId = Guid.NewGuid();
        factory.Api.Answer(HttpMethod.Post, ApiReject(requestId), HttpStatusCode.NotFound,
            RegistrationSpec.ErrorBody(RegistrationSpec.NotFoundCode, RegistrationSpec.NotFoundDescription));

        var result = await adminApi.RejectRegistrationRequestAsync(requestId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RegistrationSpec.NotFoundDescription, result.ErrorMessage);
    }

    // A 429 on any other route is a generic failure flagged as too many requests, not a session expiry
    [Fact]
    public async Task ApproveRegistrationRequest_TooManyRequests_IsFlaggedAndNotUnauthorized()
    {
        var requestId = Guid.NewGuid();
        factory.Api.Answer(HttpMethod.Post, ApiApprove(requestId), HttpStatusCode.TooManyRequests);

        var result = await adminApi.ApproveRegistrationRequestAsync(requestId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsTooManyRequests);
        Assert.False(result.IsUnauthorized);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    // Approve's 429 travels through ApiResult.Map, as does list's through the generic read
    [Fact]
    public async Task ListRegistrationRequests_TooManyRequests_IsFlagged()
    {
        factory.Api.Answer(HttpMethod.Get, ApiRegistrationRequests, HttpStatusCode.TooManyRequests);

        var result = await adminApi.ListRegistrationRequestsAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsTooManyRequests);
    }

    private static void AssertRequest(PendingRequest expected, RegistrationRequestResponse actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Email, actual.Email);
        Assert.Equal(expected.RequestedAt, actual.RequestedAt.ToUniversalTime());
        Assert.Equal(expected.ExpiresAt, actual.ExpiresAt.ToUniversalTime());
    }
}

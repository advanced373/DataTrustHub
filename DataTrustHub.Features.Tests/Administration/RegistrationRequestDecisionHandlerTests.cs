using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Features._Shared.Registration;
using DataTrustHub.Features.Administration.ApproveRegistrationRequest;
using DataTrustHub.Features.Administration.RejectRegistrationRequest;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataTrustHub.Features.Tests.Administration;

// #172 API 2 AC3, AC4 and AC5 on the approve and reject handlers, with fakes and a frozen clock: approve stages
// the user and the removal of the request and saves once; a refusal stages nothing and commits nothing
public class RegistrationRequestDecisionHandlerTests
{
    private const string Email = "Decision.Requester@Example.com";
    private const string NormalizedEmail = "decision.requester@example.com";
    private const int LifetimeDays = 14;

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(LifetimeDays);
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    private readonly FakeUserRepository _users = new();
    private readonly FakeRegistrationRequestRepository _registrationRequests = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly TestClock _clock = new();

    // --- AC3: approve a pending request ---

    [Fact]
    public async Task Approve_PendingRequest_ReturnsTheIdOfTheAddedUser()
    {
        var request = _registrationRequests.Seed(Email, _clock.UtcNow);

        var result = await ApproveAsync(request.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(Assert.Single(_users.Added).Id, result.Value);
    }

    [Fact]
    public async Task Approve_PendingRequest_AddsAUserWithTheRequestsEmailAndHashAndRoleUser()
    {
        var request = _registrationRequests.Seed(Email, _clock.UtcNow);

        await ApproveAsync(request.Id);

        var user = Assert.Single(_users.Added);
        Assert.Equal((NormalizedEmail, request.HashedPassword, UserRoles.User), (user.Email, user.HashedPassword, user.Role));
    }

    [Fact]
    public async Task Approve_PendingRequest_RemovesTheRequestAndSavesOnce()
    {
        var request = _registrationRequests.Seed(Email, _clock.UtcNow);

        await ApproveAsync(request.Id);

        Assert.Same(request, Assert.Single(_registrationRequests.Removed));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Approve_RequestExactlyTheLifetimeOld_Succeeds()
    {
        var request = _registrationRequests.Seed(Email, _clock.UtcNow - Lifetime);

        Assert.True((await ApproveAsync(request.Id)).IsSuccess);
    }

    // --- AC4: approve refused ---

    [Fact]
    public async Task Approve_UnknownId_FailsWithNotFoundWithoutCommit()
    {
        var result = await ApproveAsync(Guid.NewGuid());

        AssertApproveRefused(result, ErrorType.NotFound,
            AdminErrorCodes.RegistrationRequestNotFound, AdminErrorMessages.RegistrationRequestNotFound);
    }

    [Fact]
    public async Task Approve_ExpiredRequest_FailsWithNotFoundWithoutCommit()
    {
        var request = _registrationRequests.Seed(Email, _clock.UtcNow - Lifetime - OneSecond);

        var result = await ApproveAsync(request.Id);

        AssertApproveRefused(result, ErrorType.NotFound,
            AdminErrorCodes.RegistrationRequestNotFound, AdminErrorMessages.RegistrationRequestNotFound);
    }

    [Fact]
    public async Task Approve_EmailAlreadyHasAnAccount_FailsWithEmailAlreadyInUseWithoutCommit()
    {
        var request = _registrationRequests.Seed(Email, _clock.UtcNow);
        _users.Seed(Email);

        var result = await ApproveAsync(request.Id);

        AssertApproveRefused(result, ErrorType.Conflict, AuthErrorCodes.EmailAlreadyInUse, AuthErrorMessages.EmailAlreadyInUse);
    }

    // U1 under concurrency: another account for the email was saved first, so the unique index refuses this save
    [Fact]
    public async Task Approve_UniqueIndexViolationOnSave_FailsWithEmailAlreadyInUse()
    {
        var request = _registrationRequests.Seed(Email, _clock.UtcNow);
        var unitOfWork = new UniqueViolationUnitOfWork();

        var result = await ApproveAsync(request.Id, unitOfWork);

        Assert.True(result.IsFailure);
        Assert.Equal((ErrorType.Conflict, AuthErrorCodes.EmailAlreadyInUse), (result.Error.Type, result.Error.Code));
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    // Only a unique-index violation is the conflict; any other save failure is not turned into it (#121)
    [Fact]
    public async Task Approve_OtherSaveFailure_IsNotTurnedIntoAConflict()
    {
        var request = _registrationRequests.Seed(Email, _clock.UtcNow);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ApproveAsync(request.Id, new FailingUnitOfWork()));

        Assert.Equal(FailingUnitOfWork.FailureMessage, exception.Message);
    }

    // --- AC5: reject ---

    [Fact]
    public async Task Reject_PendingRequest_RemovesItAndSavesOnce()
    {
        var request = _registrationRequests.Seed(Email, _clock.UtcNow);

        var result = await RejectAsync(request.Id);

        Assert.True(result.IsSuccess);
        Assert.Same(request, Assert.Single(_registrationRequests.Removed));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Reject_PendingRequest_AddsNoUser()
    {
        var request = _registrationRequests.Seed(Email, _clock.UtcNow);

        await RejectAsync(request.Id);

        Assert.Empty(_users.Added);
    }

    [Fact]
    public async Task Reject_ExpiredRequest_RemovesItToo()
    {
        var request = _registrationRequests.Seed(Email, _clock.UtcNow - Lifetime - OneSecond);

        var result = await RejectAsync(request.Id);

        Assert.True(result.IsSuccess);
        Assert.Same(request, Assert.Single(_registrationRequests.Removed));
    }

    [Fact]
    public async Task Reject_UnknownId_FailsWithNotFoundWithoutCommit()
    {
        var result = await RejectAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(
            (ErrorType.NotFound, AdminErrorCodes.RegistrationRequestNotFound, AdminErrorMessages.RegistrationRequestNotFound),
            (result.Error.Type, result.Error.Code, result.Error.Description));
        Assert.Empty(_registrationRequests.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // --- helpers ---

    private Task<Result<Guid>> ApproveAsync(Guid requestId, IUnitOfWork? unitOfWork = null) =>
        new ApproveRegistrationRequestHandler(
            _registrationRequests, _users, unitOfWork ?? _unitOfWork,
            Options.Create(new RegistrationOptions { PendingRequestLifetimeDays = LifetimeDays }), _clock)
        .Handle(new ApproveRegistrationRequestCommand(requestId), default);

    private Task<Result<Guid>> RejectAsync(Guid requestId) =>
        new RejectRegistrationRequestHandler(_registrationRequests, _unitOfWork)
            .Handle(new RejectRegistrationRequestCommand(requestId), default);

    private void AssertApproveRefused(Result<Guid> result, ErrorType type, string code, string message)
    {
        Assert.True(result.IsFailure);
        Assert.Equal((type, code, message), (result.Error.Type, result.Error.Code, result.Error.Description));
        Assert.Empty(_users.Added);
        Assert.Empty(_registrationRequests.Removed);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }
}

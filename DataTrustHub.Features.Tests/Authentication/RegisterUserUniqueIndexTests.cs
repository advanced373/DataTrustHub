using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Features._Shared.Registration;
using DataTrustHub.Features.Authentication.RegisterUser;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

// #172 (spec step 5, AC3): a concurrent registration request with the same email saved first, so the unique
// index on RegistrationRequests.Email rejects this save. The use case answers 409 Registration.AlreadyPending,
// the error its check gives, and does not turn other save failures into it. Built directly with fakes, so no
// database is involved.
public class RegisterUserUniqueIndexTests
{
    private const string Email = "Unique.Register@Example.com";
    private const string Password = "Password1!";

    private readonly FakeUserRepository _users = new();
    private readonly FakeRegistrationRequestRepository _registrationRequests = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly TestClock _clock = new();
    private readonly UniqueViolationUnitOfWork _conflictingUnitOfWork = new();

    [Fact]
    public async Task RegisterUser_UniqueIndexRejectsSave_FailsWithRegistrationAlreadyPendingConflict()
    {
        var result = await Handler(_conflictingUnitOfWork).Handle(new RegisterUserCommand(Email, Password), default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            (ErrorType.Conflict, AuthErrorCodes.RegistrationAlreadyPending, AuthErrorMessages.RegistrationAlreadyPending),
            (result.Error.Type, result.Error.Code, result.Error.Description));
        Assert.Equal(1, _conflictingUnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task RegisterUser_UniqueIndexRejectsSave_FailsWithTheSameErrorAsThePendingCheck()
    {
        var fromIndex = await Handler(_conflictingUnitOfWork).Handle(new RegisterUserCommand(Email, Password), default);
        _registrationRequests.Seed(Email, _clock.UtcNow);
        var fromCheck = await Handler(new CountingUnitOfWork()).Handle(new RegisterUserCommand(Email, Password), default);

        Assert.Equal(fromCheck.Error, fromIndex.Error);
    }

    [Fact]
    public async Task RegisterUser_OtherSaveFailure_IsNotTurnedIntoAConflict()
    {
        var handler = Handler(new FailingUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new RegisterUserCommand(Email, Password), default));
    }

    private RegisterUserHandler Handler(IUnitOfWork unitOfWork) =>
        new(_users, _registrationRequests, _hasher, unitOfWork, Options.Create(new RegistrationOptions()), _clock);
}

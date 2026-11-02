using DataTrustHub.Features.Authentication.RegisterUser;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

// #121 AC1 (U1) for RegisterUser: a concurrent registration with the same email saved first, so the unique
// index rejects this save. The use case answers with its existing 409 EmailAlreadyInUse, the error its check
// gives, and does not turn other save failures into it. Built directly with fakes, so no database is involved.
public class RegisterUserUniqueIndexTests
{
    private const string Email = "Unique.Register@Example.com";
    private const string Password = "Password1!";

    private readonly FakeUserRepository _users = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly UniqueViolationUnitOfWork _conflictingUnitOfWork = new();

    [Fact]
    public async Task RegisterUser_UniqueIndexRejectsSave_FailsWithEmailAlreadyInUseConflict()
    {
        var result = await new RegisterUserHandler(_users, _hasher, _conflictingUnitOfWork)
            .Handle(new RegisterUserCommand(Email, Password), default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            (ErrorType.Conflict, AuthErrorCodes.EmailAlreadyInUse, AuthErrorMessages.EmailAlreadyInUse),
            (result.Error.Type, result.Error.Code, result.Error.Description));
        Assert.Equal(1, _conflictingUnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task RegisterUser_UniqueIndexRejectsSave_FailsWithTheSameErrorAsTheCheck()
    {
        var fromIndex = await new RegisterUserHandler(_users, _hasher, _conflictingUnitOfWork)
            .Handle(new RegisterUserCommand(Email, Password), default);
        _users.Seed(Email);
        var fromCheck = await new RegisterUserHandler(_users, _hasher, new CountingUnitOfWork())
            .Handle(new RegisterUserCommand(Email, Password), default);

        Assert.Equal(fromCheck.Error, fromIndex.Error);
    }

    [Fact]
    public async Task RegisterUser_OtherSaveFailure_IsNotTurnedIntoAConflict()
    {
        var handler = new RegisterUserHandler(_users, _hasher, new FailingUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new RegisterUserCommand(Email, Password), default));
    }
}

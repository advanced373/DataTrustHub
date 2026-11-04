using DataTrustHub.Features._Shared.Jwt;
using DataTrustHub.Features._Shared.Registration;
using DataTrustHub.Features.Authentication.LoginUser;
using DataTrustHub.Features.Authentication.RegisterUser;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

// ADR-0012 step 3c: register and login run on repositories. Register commits once, only on success (since
// #172 it stores a registration request, not a user);
// login never commits. The handlers are built directly with fakes, so no database is involved.
public class AuthenticationHandlerTests
{
    private const string Email = "Handler.Auth@Example.com";
    private const string NormalizedEmail = "handler.auth@example.com";
    private const string VariantEmail = " HANDLER.Auth@Example.com ";
    private const string UnknownVariantEmail = " Nobody@Example.COM ";
    private const string Password = "Password1!";
    private const string WrongPassword = "WrongPassword1!";
    private const string UnknownEmail = "nobody@example.com";

    private readonly FakeUserRepository _users = new();
    private readonly FakeRegistrationRequestRepository _registrationRequests = new();
    private readonly TestClock _clock = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly RecordingJwtTokenGenerator _jwtGenerator = new();

    // --- RegisterUser ---

    // #172: register submits a registration request; no user is added until it is approved
    [Fact]
    public async Task RegisterUser_NewEmail_SubmitsARequestWithNormalizedEmailAndHashedPasswordAndCommitsOnce()
    {
        var result = await RegisterHandler().Handle(new RegisterUserCommand(Email, Password), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(_users.Added);
        var added = Assert.Single(_registrationRequests.Added);
        Assert.Equal((result.Value, NormalizedEmail, _clock.UtcNow), (added.Id, added.Email, added.RequestedAt));
        Assert.NotEqual(Password, added.HashedPassword);
        Assert.True(_hasher.VerifyHashedPassword(added.HashedPassword, Password));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task RegisterUser_EmailInUse_FailsWithConflictWithoutCommit()
    {
        _users.Seed(Email);

        var result = await RegisterHandler().Handle(new RegisterUserCommand(Email, Password), default);

        Assert.Equal(
            (ErrorType.Conflict, AuthErrorCodes.EmailAlreadyInUse, AuthErrorMessages.EmailAlreadyInUse),
            (result.Error.Type, result.Error.Code, result.Error.Description));
        Assert.Empty(_users.Added);
        Assert.Empty(_registrationRequests.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // --- LoginUser ---

    // The seeded user's password is its own email (see FakeUserRepository.Seed)
    [Fact]
    public async Task LoginUser_ValidCredentials_IssuesTokenForThatUserWithoutCommit()
    {
        var user = _users.Seed(Email);

        var result = await LoginHandler().Handle(new LoginUserCommand(Email, Email), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecordingJwtTokenGenerator.Token, result.Value);
        Assert.Equal((user.Id, user.Email, user.Role), Assert.Single(_jwtGenerator.Calls));
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task LoginUser_UnknownEmailAndWrongPassword_FailWithTheSameErrorAndIssueNoToken()
    {
        _users.Seed(Email);

        var unknown = await LoginHandler().Handle(new LoginUserCommand(UnknownEmail, Password), default);
        var wrongPassword = await LoginHandler().Handle(new LoginUserCommand(Email, WrongPassword), default);

        Assert.True(unknown.IsFailure);
        Assert.Equal(AuthErrorCodes.InvalidCredentials, unknown.Error.Code);
        Assert.Equal(AuthErrorMessages.InvalidCredentials, unknown.Error.Description);
        Assert.Equal(
            (unknown.Error.Type, unknown.Error.Code, unknown.Error.Description),
            (wrongPassword.Error.Type, wrongPassword.Error.Code, wrongPassword.Error.Description));
        Assert.Empty(_jwtGenerator.Calls);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // --- AC2 and AC4 of #123: a case or space variant is the same address (U7) ---

    [Fact]
    public async Task RegisterUser_VariantOfAnExistingEmail_FailsWithConflictWithoutCommit()
    {
        _users.Seed(NormalizedEmail);

        var result = await RegisterHandler().Handle(new RegisterUserCommand(VariantEmail, Password), default);

        Assert.Equal((ErrorType.Conflict, AuthErrorCodes.EmailAlreadyInUse), (result.Error.Type, result.Error.Code));
        Assert.Empty(_users.Added);
        Assert.Empty(_registrationRequests.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task LoginUser_VariantOfTheEmail_IssuesTokenWithTheNormalizedEmail()
    {
        var user = _users.Seed(NormalizedEmail);

        var result = await LoginHandler().Handle(new LoginUserCommand(VariantEmail, NormalizedEmail), default);

        Assert.True(result.IsSuccess);
        Assert.Equal((user.Id, NormalizedEmail, user.Role), Assert.Single(_jwtGenerator.Calls));
    }

    [Fact]
    public async Task LoginUser_UnknownVariantAndWrongPasswordOnAVariant_FailWithInvalidCredentials()
    {
        _users.Seed(NormalizedEmail);

        var unknown = await LoginHandler().Handle(new LoginUserCommand(UnknownVariantEmail, Password), default);
        var wrongPassword = await LoginHandler().Handle(new LoginUserCommand(VariantEmail, WrongPassword), default);

        Assert.Equal(
            (AuthErrorCodes.InvalidCredentials, AuthErrorMessages.InvalidCredentials),
            (unknown.Error.Code, unknown.Error.Description));
        Assert.Equal(
            (unknown.Error.Type, unknown.Error.Code, unknown.Error.Description),
            (wrongPassword.Error.Type, wrongPassword.Error.Code, wrongPassword.Error.Description));
        Assert.Empty(_jwtGenerator.Calls);
    }

    private RegisterUserHandler RegisterHandler() =>
        new(_users, _registrationRequests, _hasher, _unitOfWork, Options.Create(new RegistrationOptions()), _clock);

    private LoginUserHandler LoginHandler() => new(_users, _hasher, _jwtGenerator);

    private sealed class RecordingJwtTokenGenerator : IJwtTokenGenerator
    {
        internal const string Token = "recorded-token";

        internal List<(Guid UserId, string Email, string Role)> Calls { get; } = [];

        public string Generate(Guid userId, string email, string role)
        {
            Calls.Add((userId, email, role));
            return Token;
        }
    }
}

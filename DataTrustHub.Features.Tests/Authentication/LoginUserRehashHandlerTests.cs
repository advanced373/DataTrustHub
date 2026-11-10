using System.Security.Cryptography;
using System.Text;
using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Jwt;
using DataTrustHub.Features.Authentication.LoginUser;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Xunit;
using InfrastructurePasswordHasher = DataTrustHub.Infrastructure.Services.PasswordHasher;
using PasswordVerificationResult = DataTrustHub.Domain.Users.PasswordVerificationResult;

namespace DataTrustHub.Features.Tests.Authentication;

// #167: sign-in replaces a password hash in an older format with a new one (one save), commits nothing in any
// other case, and spends hashing work for an unknown email (ADR-0025). Built directly with fakes; the fake
// hasher's legacy prefix stands for a hash in an older format. One test uses the real hasher end to end.
public class LoginUserRehashHandlerTests
{
    private const string Email = "rehash.handler@example.com";
    private const string UnknownEmail = "nobody.rehash@example.com";
    private const string Password = "Password1!";
    private const string WrongPassword = "WrongPassword1!";
    private const byte IdentityV3FormatMarker = 0x01;

    private readonly FakeUserRepository _users = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly StubJwtTokenGenerator _jwtGenerator = new();
    private readonly AuditScope _auditScope = new();

    // --- AC7 ---

    [Fact]
    public async Task LoginUser_LegacyHashAndRightPassword_IssuesToken()
    {
        SeedWithHash(FakePasswordHasher.LegacyHashPrefix + Password);

        var result = await Handler(_hasher, _unitOfWork).Handle(new LoginUserCommand(Email, Password), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(StubJwtTokenGenerator.Token, result.Value);
    }

    [Fact]
    public async Task LoginUser_LegacyHashAndRightPassword_ReplacesTheHashAndCommitsOnce()
    {
        var user = SeedWithHash(FakePasswordHasher.LegacyHashPrefix + Password);

        await Handler(_hasher, _unitOfWork).Handle(new LoginUserCommand(Email, Password), default);

        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(user.HashedPassword, Password));
        Assert.Same(user, Assert.Single(_users.Updated));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task LoginUser_SecondLoginAfterRehash_DoesNotCommitAgain()
    {
        SeedWithHash(FakePasswordHasher.LegacyHashPrefix + Password);
        var handler = Handler(_hasher, _unitOfWork);

        await handler.Handle(new LoginUserCommand(Email, Password), default);
        var second = await handler.Handle(new LoginUserCommand(Email, Password), default);

        Assert.True(second.IsSuccess);
        Assert.Single(_users.Updated);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task LoginUser_RealHasherAndSha256Hash_StoresAV3HashThatVerifiesSuccess()
    {
        var realHasher = new InfrastructurePasswordHasher(Options.Create(new PasswordHasherOptions()));
        var user = SeedWithHash(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Password))));

        var result = await Handler(realHasher, _unitOfWork).Handle(new LoginUserCommand(Email, Password), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(IdentityV3FormatMarker, Convert.FromBase64String(user.HashedPassword)[0]);
        Assert.Equal(PasswordVerificationResult.Success, realHasher.VerifyHashedPassword(user.HashedPassword, Password));
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task LoginUser_LegacyHashAndSaveFails_PropagatesTheFailure()
    {
        SeedWithHash(FakePasswordHasher.LegacyHashPrefix + Password);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(_hasher, new FailingUnitOfWork()).Handle(new LoginUserCommand(Email, Password), default));

        Assert.Equal(FailingUnitOfWork.FailureMessage, exception.Message);
        Assert.Empty(_jwtGenerator.Calls);
    }

    // AC3 of #134: a sign-in that also replaces the password hash (U8) is recorded with its actor too
    [Fact]
    public async Task LoginUser_LegacyHashAndRightPassword_SetsTheSignedInUserAsTheActor()
    {
        var user = SeedWithHash(FakePasswordHasher.LegacyHashPrefix + Password);

        await Handler(_hasher, _unitOfWork).Handle(new LoginUserCommand(Email, Password), default);

        Assert.Equal(user.Id, _auditScope.ActorUserId);
    }

    // --- AC8 ---

    [Fact]
    public async Task LoginUser_CurrentHash_IssuesTokenWithoutReplacingTheHashOrCommitting()
    {
        var currentHash = FakePasswordHasher.HashPrefix + Password;
        var user = SeedWithHash(currentHash);

        var result = await Handler(_hasher, _unitOfWork).Handle(new LoginUserCommand(Email, Password), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(currentHash, user.HashedPassword);
        Assert.Empty(_users.Updated);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    // --- AC9 ---

    [Fact]
    public async Task LoginUser_LegacyHashAndWrongPassword_FailsWithInvalidCredentialsAndKeepsTheHash()
    {
        var legacyHash = FakePasswordHasher.LegacyHashPrefix + Password;
        var user = SeedWithHash(legacyHash);

        var result = await Handler(_hasher, _unitOfWork).Handle(new LoginUserCommand(Email, WrongPassword), default);

        AssertInvalidCredentials(result);
        Assert.Equal(legacyHash, user.HashedPassword);
        Assert.Empty(_users.Updated);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
        Assert.Empty(_jwtGenerator.Calls);
    }

    // --- AC10 ---

    [Fact]
    public async Task LoginUser_UnknownEmail_FailsWithInvalidCredentialsWithoutCommit()
    {
        SeedWithHash(FakePasswordHasher.HashPrefix + Password);

        var result = await Handler(_hasher, _unitOfWork).Handle(new LoginUserCommand(UnknownEmail, Password), default);

        AssertInvalidCredentials(result);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
        Assert.Empty(_jwtGenerator.Calls);
    }

    [Fact]
    public async Task LoginUser_UnknownEmail_HashesThePasswordOnce()
    {
        await Handler(_hasher, _unitOfWork).Handle(new LoginUserCommand(UnknownEmail, Password), default);

        Assert.Equal(1, _hasher.HashPasswordCalls);
    }

    private User SeedWithHash(string hashedPassword)
    {
        var user = User.Register(Email, hashedPassword);
        _users.Stored.Add(user);
        return user;
    }

    private LoginUserHandler Handler(IPasswordHasher hasher, IUnitOfWork unitOfWork) =>
        new(_users, hasher, _jwtGenerator, unitOfWork, _auditScope);

    private static void AssertInvalidCredentials(Result<string> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(
            (AuthErrorCodes.InvalidCredentials, AuthErrorMessages.InvalidCredentials),
            (result.Error.Code, result.Error.Description));
    }

    private sealed class StubJwtTokenGenerator : IJwtTokenGenerator
    {
        internal const string Token = "stub-token";

        internal List<Guid> Calls { get; } = [];

        public string Generate(Guid userId, string email, string role)
        {
            Calls.Add(userId);
            return Token;
        }
    }
}

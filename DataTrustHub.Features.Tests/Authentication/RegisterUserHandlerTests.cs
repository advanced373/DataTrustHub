using DataTrustHub.Features._Shared.Registration;
using DataTrustHub.Features.Authentication.RegisterUser;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataTrustHub.Features.Tests.Authentication;

// #172 AC3, AC4 and AC5 on the handler, with fakes and a hand-moved clock: the purge of expired registration
// requests comes first, then the account check, the pending check and the cap on pending requests.
// A refusal stages nothing and commits nothing.
public class RegisterUserHandlerTests
{
    private const string Email = "Handler.Registration@Example.com";
    private const string VariantEmail = " HANDLER.registration@example.COM\t";
    private const string OtherEmailFormat = "pending{0}@example.com";
    private const string Password = "Password1!";
    private const int LifetimeDays = 14;
    private const int MaxPendingRequests = 3;

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(LifetimeDays);
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    private readonly FakeUserRepository _users = new();
    private readonly FakeRegistrationRequestRepository _registrationRequests = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly TestClock _clock = new();

    // --- AC4: the purge ---

    [Fact]
    public async Task RegisterUser_Always_PurgesRequestsOlderThanTheLifetimeFromNow()
    {
        await RegisterAsync(Email);

        Assert.Equal(_clock.UtcNow - Lifetime, Assert.Single(_registrationRequests.PurgeCutoffs));
    }

    [Fact]
    public async Task RegisterUser_EmailInUse_StillPurgesExpiredRequests()
    {
        _users.Seed(Email);
        _registrationRequests.Seed(OtherEmail(0), _clock.UtcNow - Lifetime - OneSecond);

        await RegisterAsync(Email);

        Assert.Empty(_registrationRequests.Stored);
    }

    [Fact]
    public async Task RegisterUser_EmailWithAnExpiredRequest_SubmitsANewRequest()
    {
        _registrationRequests.Seed(Email, _clock.UtcNow - Lifetime - OneSecond);

        var result = await RegisterAsync(Email);

        Assert.True(result.IsSuccess);
        Assert.Single(_registrationRequests.Added);
        Assert.Equal(1, _unitOfWork.SaveChangesCalls);
    }

    // --- AC3: one pending request per email ---

    [Theory]
    [InlineData(Email)]
    [InlineData(VariantEmail)]
    public async Task RegisterUser_EmailWithAPendingRequest_FailsWithAlreadyPendingWithoutCommit(string email)
    {
        _registrationRequests.Seed(Email, _clock.UtcNow);

        var result = await RegisterAsync(email);

        AssertRefused(result, ErrorType.Conflict, AuthErrorCodes.RegistrationAlreadyPending, AuthErrorMessages.RegistrationAlreadyPending);
    }

    // A request exactly as old as the lifetime has not expired yet ("older than" the lifetime expires)
    [Fact]
    public async Task RegisterUser_PendingRequestExactlyAsOldAsTheLifetime_StillBlocksTheEmail()
    {
        _registrationRequests.Seed(Email, _clock.UtcNow - Lifetime);

        var result = await RegisterAsync(Email);

        AssertRefused(result, ErrorType.Conflict, AuthErrorCodes.RegistrationAlreadyPending, AuthErrorMessages.RegistrationAlreadyPending);
    }

    [Fact]
    public async Task RegisterUser_EmailWithAnAccountAndAPendingRequest_FailsWithEmailAlreadyInUse()
    {
        _users.Seed(Email);
        _registrationRequests.Seed(Email, _clock.UtcNow);

        var result = await RegisterAsync(Email);

        AssertRefused(result, ErrorType.Conflict, AuthErrorCodes.EmailAlreadyInUse, AuthErrorMessages.EmailAlreadyInUse);
    }

    // --- AC5: the cap on pending requests ---

    [Fact]
    public async Task RegisterUser_QueueFull_FailsWithQueueFullUnavailableWithoutCommit()
    {
        SeedPending(MaxPendingRequests);

        var result = await RegisterAsync(Email);

        AssertRefused(result, ErrorType.Unavailable, AuthErrorCodes.RegistrationQueueFull, AuthErrorMessages.RegistrationQueueFull);
    }

    [Fact]
    public async Task RegisterUser_OneBelowTheCap_SubmitsTheRequest()
    {
        SeedPending(MaxPendingRequests - 1);

        var result = await RegisterAsync(Email);

        Assert.True(result.IsSuccess);
        Assert.Single(_registrationRequests.Added);
    }

    [Fact]
    public async Task RegisterUser_QueueFullOfExpiredRequests_SubmitsTheRequest()
    {
        SeedPending(MaxPendingRequests, _clock.UtcNow - Lifetime - OneSecond);

        var result = await RegisterAsync(Email);

        Assert.True(result.IsSuccess);
        Assert.Single(_registrationRequests.Added);
    }

    // The email checks come before the cap, so a full queue does not hide a 409
    [Fact]
    public async Task RegisterUser_QueueFullAndEmailPending_FailsWithAlreadyPending()
    {
        _registrationRequests.Seed(Email, _clock.UtcNow);
        SeedPending(MaxPendingRequests - 1);

        var result = await RegisterAsync(Email);

        Assert.Equal(AuthErrorCodes.RegistrationAlreadyPending, result.Error.Code);
    }

    // --- helpers ---

    private Task<Result<Guid>> RegisterAsync(string email) =>
        new RegisterUserHandler(_users, _registrationRequests, _hasher, _unitOfWork, Options.Create(new RegistrationOptions
        {
            PendingRequestLifetimeDays = LifetimeDays,
            MaxPendingRequests = MaxPendingRequests
        }), _clock).Handle(new RegisterUserCommand(email, Password), default);

    private void SeedPending(int count, DateTime? requestedAt = null)
    {
        for (var i = 0; i < count; i++) _registrationRequests.Seed(OtherEmail(i), requestedAt ?? _clock.UtcNow);
    }

    private static string OtherEmail(int index) => string.Format(OtherEmailFormat, index);

    private void AssertRefused(Result<Guid> result, ErrorType type, string code, string message)
    {
        Assert.True(result.IsFailure);
        Assert.Equal((type, code, message), (result.Error.Type, result.Error.Code, result.Error.Description));
        Assert.Empty(_users.Added);
        Assert.Empty(_registrationRequests.Added);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }
}

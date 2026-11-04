using DataTrustHub.Domain.Registrations;
using DataTrustHub.SharedKernel;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

// #172: a registration request holds the normalized email (U7), the password hash and the UTC time it was made;
// it has expired once it is older than the pending lifetime (R2); approving a pending one gives the user (R5)
public class RegistrationRequestTests
{
    private const string VariantEmail = " Ion@Firma.RO ";
    private const string NormalizedEmail = "ion@firma.ro";
    private const string Hash = "hash";
    private const string NotFoundCode = "RegistrationRequest.NotFound";
    private const string NotFoundMessage = "The registration request was not found.";

    private static readonly DateTime RequestedAt = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);

    [Fact]
    public void Submit_EmailWithCaseAndSurroundingSpaces_StoresTheNormalizedEmail()
    {
        Assert.Equal(NormalizedEmail, RegistrationRequest.Submit(VariantEmail, Hash, RequestedAt).Email);
    }

    [Fact]
    public void Submit_StoresTheHashAndTheTime()
    {
        var request = RegistrationRequest.Submit(NormalizedEmail, Hash, RequestedAt);

        Assert.Equal((Hash, RequestedAt), (request.HashedPassword, request.RequestedAt));
    }

    [Fact]
    public void Submit_GivesEachRequestItsOwnId()
    {
        var first = RegistrationRequest.Submit(NormalizedEmail, Hash, RequestedAt);
        var second = RegistrationRequest.Submit(NormalizedEmail, Hash, RequestedAt);

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Submit_TimeNotInUtc_Throws(DateTimeKind kind)
    {
        var time = DateTime.SpecifyKind(RequestedAt, kind);

        Assert.Throws<ArgumentException>(() => RegistrationRequest.Submit(NormalizedEmail, Hash, time));
    }

    [Fact]
    public void IsExpired_OlderThanTheLifetime_IsTrue()
    {
        var request = RegistrationRequest.Submit(NormalizedEmail, Hash, RequestedAt);

        Assert.True(request.IsExpired(RequestedAt + Lifetime + OneTick, Lifetime));
    }

    [Fact]
    public void IsExpired_ExactlyTheLifetimeOld_IsFalse()
    {
        var request = RegistrationRequest.Submit(NormalizedEmail, Hash, RequestedAt);

        Assert.False(request.IsExpired(RequestedAt + Lifetime, Lifetime));
    }

    [Fact]
    public void IsExpired_Recent_IsFalse()
    {
        var request = RegistrationRequest.Submit(NormalizedEmail, Hash, RequestedAt);

        Assert.False(request.IsExpired(RequestedAt, Lifetime));
    }

    // --- API 2 AC3/AC4: approve ---

    [Fact]
    public void Approve_PendingRequest_ReturnsAUserWithTheNormalizedEmailTheHashAndRoleUser()
    {
        var request = RegistrationRequest.Submit(VariantEmail, Hash, RequestedAt);

        var result = request.Approve(RequestedAt, Lifetime);

        Assert.True(result.IsSuccess);
        Assert.Equal((NormalizedEmail, Hash, UserRoles.User), (result.Value.Email, result.Value.HashedPassword, result.Value.Role));
    }

    [Fact]
    public void Approve_PendingRequest_GivesTheUserANewId()
    {
        var request = RegistrationRequest.Submit(NormalizedEmail, Hash, RequestedAt);

        var user = request.Approve(RequestedAt, Lifetime).Value;

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.False(user.IsAdmin);
    }

    [Fact]
    public void Approve_ExactlyTheLifetimeOld_Succeeds()
    {
        var request = RegistrationRequest.Submit(NormalizedEmail, Hash, RequestedAt);

        Assert.True(request.Approve(RequestedAt + Lifetime, Lifetime).IsSuccess);
    }

    [Fact]
    public void Approve_ExpiredRequest_FailsWithNotFound()
    {
        var request = RegistrationRequest.Submit(NormalizedEmail, Hash, RequestedAt);

        var result = request.Approve(RequestedAt + Lifetime + OneTick, Lifetime);

        Assert.True(result.IsFailure);
        Assert.Equal((ErrorType.NotFound, NotFoundCode, NotFoundMessage), (result.Error.Type, result.Error.Code, result.Error.Description));
    }
}

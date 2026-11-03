using DataTrustHub.Domain.Registrations;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

// #172: a registration request holds the normalized email (U7), the password hash and the UTC time it was made;
// it has expired once it is older than the pending lifetime (R2)
public class RegistrationRequestTests
{
    private const string VariantEmail = " Ion@Firma.RO ";
    private const string NormalizedEmail = "ion@firma.ro";
    private const string Hash = "hash";

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
}

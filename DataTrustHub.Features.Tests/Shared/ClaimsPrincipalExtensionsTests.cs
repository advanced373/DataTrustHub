using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using DataTrustHub.Features._Shared.Extensions;
using Xunit;

namespace DataTrustHub.Features.Tests.Shared;

// AC1 and AC3 of #119: one extension on ClaimsPrincipal reads the caller id from the JWT: sub first, then
// NameIdentifier, parsed as a Guid, null otherwise. A sub that is present but not a Guid gives null even when
// NameIdentifier is a Guid, because the fallback only applies to a missing sub
public class ClaimsPrincipalExtensionsTests
{
    private const string ExtensionTypeName = "DataTrustHub.Features._Shared.Extensions.ClaimsPrincipalExtensions";
    private const string MethodName = "GetUserId";
    private const string AuthenticationType = "Test";
    private const string NotAGuid = "not-a-guid";

    private static readonly MethodInfo GetUserIdMethod = typeof(FeaturesRegistration).Assembly
        .GetType(ExtensionTypeName, throwOnError: true)!
        .GetMethod(MethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, [typeof(ClaimsPrincipal)])!;

    private readonly Guid _subId = Guid.NewGuid();
    private readonly Guid _nameIdentifierId = Guid.NewGuid();

    // --- the claim combinations (AC3) ---

    [Fact]
    public void GetUserId_SubIsGuid_ReturnsTheSubId()
    {
        var user = UserWith(new Claim(JwtRegisteredClaimNames.Sub, _subId.ToString()));

        Assert.Equal(_subId, GetUserId(user));
    }

    [Fact]
    public void GetUserId_SubAndNameIdentifierAreGuids_ReturnsTheSubId()
    {
        var user = UserWith(
            new Claim(ClaimTypes.NameIdentifier, _nameIdentifierId.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, _subId.ToString()));

        Assert.Equal(_subId, GetUserId(user));
    }

    [Fact]
    public void GetUserId_NoSubAndNameIdentifierIsGuid_ReturnsTheNameIdentifierId()
    {
        var user = UserWith(new Claim(ClaimTypes.NameIdentifier, _nameIdentifierId.ToString()));

        Assert.Equal(_nameIdentifierId, GetUserId(user));
    }

    [Fact]
    public void GetUserId_SubIsNotAGuidAndNameIdentifierIsGuid_ReturnsNull()
    {
        var user = UserWith(
            new Claim(JwtRegisteredClaimNames.Sub, NotAGuid),
            new Claim(ClaimTypes.NameIdentifier, _nameIdentifierId.ToString()));

        Assert.Null(GetUserId(user));
    }

    [Fact]
    public void GetUserId_SubIsEmptyAndNameIdentifierIsGuid_ReturnsNull()
    {
        var user = UserWith(
            new Claim(JwtRegisteredClaimNames.Sub, string.Empty),
            new Claim(ClaimTypes.NameIdentifier, _nameIdentifierId.ToString()));

        Assert.Null(GetUserId(user));
    }

    [Fact]
    public void GetUserId_SubIsNotAGuidWithoutNameIdentifier_ReturnsNull()
    {
        var user = UserWith(new Claim(JwtRegisteredClaimNames.Sub, NotAGuid));

        Assert.Null(GetUserId(user));
    }

    [Fact]
    public void GetUserId_NoSubAndNameIdentifierIsNotAGuid_ReturnsNull()
    {
        var user = UserWith(new Claim(ClaimTypes.NameIdentifier, NotAGuid));

        Assert.Null(GetUserId(user));
    }

    [Fact]
    public void GetUserId_NeitherSubNorNameIdentifier_ReturnsNull()
    {
        var user = UserWith(new Claim(ClaimTypes.Name, _subId.ToString()));

        Assert.Null(GetUserId(user));
    }

    [Fact]
    public void GetUserId_UserWithoutIdentity_ReturnsNull()
    {
        Assert.Null(GetUserId(new ClaimsPrincipal()));
    }

    // The rule reads the claims, not whether the identity is authenticated; the endpoints sit behind
    // RequireAuthorization and the audit decorator checks authentication itself
    [Fact]
    public void GetUserId_UnauthenticatedIdentityWithGuidSub_ReturnsTheSubId()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, _subId.ToString())]));

        Assert.Equal(_subId, GetUserId(user));
    }

    private static ClaimsPrincipal UserWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, AuthenticationType));

    // The extension is internal to DataTrustHub.Features, which grants the tests no internals access
    private static Guid? GetUserId(ClaimsPrincipal user) => (Guid?)GetUserIdMethod.Invoke(null, [user]);
}

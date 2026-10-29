using System.Text;
using DataTrustHub.Features.Tests.Configuration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Configuration;

// AC2, AC3: a missing, empty or too short JWT signing key stops the API while its host is built, with a
// message that names the setting and where to set it, and never shows the key
public class JwtSigningKeyStartupTests
{
    private const string UserSecrets = "User Secrets";
    private const string Adr0007 = "ADR-0007";
    private const char AsciiChar = 'k';

    // 'é' is two bytes in UTF-8: half as many characters as the byte minimum make exactly the minimum
    private const char TwoByteChar = 'é';
    private const int TwoByteCharSize = 2;

    private static readonly string TooShortKey = new(AsciiChar, TestJwt.MinimumSecretBytes - 1);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void HostBuild_SigningKeyMissingOrEmpty_ThrowsNamingTheSetting(string? signingKey)
    {
        var exception = BuildHost(signingKey);

        Assert.Contains(TestJwt.SecretKey, exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void HostBuild_SigningKeyMissingOrEmpty_ThrowsSayingWhereToSetIt(string? signingKey)
    {
        var exception = BuildHost(signingKey);

        Assert.Contains(UserSecrets, exception.Message);
        Assert.Contains(Adr0007, exception.Message);
    }

    [Fact]
    public void HostBuild_SigningKeyOneByteBelowMinimum_ThrowsNamingTheSettingAndMinimum()
    {
        var exception = BuildHost(TooShortKey);

        Assert.Contains(TestJwt.SecretKey, exception.Message);
        Assert.Contains(TestJwt.MinimumSecretBytes.ToString(), exception.Message);
    }

    [Fact]
    public void HostBuild_SigningKeyTooShort_MessageDoesNotContainTheKey()
    {
        var exception = BuildHost(TooShortKey);

        Assert.DoesNotContain(TooShortKey, exception.Message);
    }

    [Fact]
    public void HostBuild_SigningKeyExactlyMinimumBytes_Starts()
    {
        var signingKey = new string(AsciiChar, TestJwt.MinimumSecretBytes);

        using var factory = new SigningKeyApiFactory(signingKey);

        Assert.NotNull(factory.Services);
    }

    [Fact]
    public void HostBuild_MultibyteKeyReachingMinimumInBytesNotCharacters_Starts()
    {
        var signingKey = new string(TwoByteChar, TestJwt.MinimumSecretBytes / TwoByteCharSize);
        Assert.Equal(TestJwt.MinimumSecretBytes, Encoding.UTF8.GetByteCount(signingKey));

        using var factory = new SigningKeyApiFactory(signingKey);

        Assert.NotNull(factory.Services);
    }

    [Fact]
    public void HostBuild_MultibyteKeyOneByteBelowMinimum_Throws()
    {
        // Fewer characters than bytes: one byte short of the minimum, counted in UTF-8
        var signingKey = new string(TwoByteChar, TestJwt.MinimumSecretBytes / TwoByteCharSize - 1) + AsciiChar;
        Assert.Equal(TestJwt.MinimumSecretBytes - 1, Encoding.UTF8.GetByteCount(signingKey));

        var exception = BuildHost(signingKey);

        Assert.Contains(TestJwt.SecretKey, exception.Message);
    }

    private static InvalidOperationException BuildHost(string? signingKey)
    {
        using var factory = new SigningKeyApiFactory(signingKey);
        return Assert.Throws<InvalidOperationException>(() => factory.Services);
    }
}

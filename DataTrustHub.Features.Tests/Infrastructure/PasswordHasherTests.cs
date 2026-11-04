using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features.Tests.Build.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Xunit;
using InfrastructurePasswordHasher = DataTrustHub.Infrastructure.Services.PasswordHasher;
using PasswordVerificationResult = DataTrustHub.Domain.Users.PasswordVerificationResult;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #167: the password hasher adapter hashes with Identity's V3 format (PBKDF2, salted), still accepts the legacy
// unsalted SHA-256 hash (asking for a re-hash), treats any malformed stored value as Failed and compares hashes
// in constant time only. Built directly, with Identity's default iteration count unless a test says otherwise
public class PasswordHasherTests
{
    private const string Password = "Password1!";
    private const string WrongPassword = "WrongPassword1!";
    private const byte IdentityV3FormatMarker = 0x01;
    private const int WeakerIterationCount = 10_000;
    private const string HasherSourceFile = "DataTrustHub.Infrastructure/Services/PasswordHasher.cs";
    // Identity V3 layout: marker (1) + PRF (4) + iteration count (4) + salt length (4) + 128-bit salt (16)
    private const int V3HeaderAndSaltLength = 29;
    private const int ArbitraryByteStep = 37;
    private const int ArbitraryByteOffset = 11;
    private const string FixedTimeEquals = "CryptographicOperations.FixedTimeEquals";

    // Comparisons that are not constant time; none may touch a hash value in the hasher
    private static readonly string[] NonConstantTimeComparisons = ["SequenceEqual", "string.Equals", "String.Equals", ".Equals("];

    // An == or != is allowed only on sizes (a length, a byte count) or literals, never on a hash value
    private static readonly Regex EqualityOperands = new(@"(?<left>[\w.]+)\s*[!=]=\s*(?<right>[\w.]+)");
    private static readonly Regex SizeOrLiteral = new(@"^(\d+|null|true|false|[\w.]*(Length|Count|SizeInBytes))$");
    private static readonly Regex LineComment = new(@"//.*$", RegexOptions.Multiline);

    private readonly InfrastructurePasswordHasher _hasher = HasherWith(new PasswordHasherOptions());

    // --- AC1 ---

    [Fact]
    public void HashPassword_AnyPassword_ReturnsAnIdentityV3HashThatIsNotThePassword()
    {
        var hash = _hasher.HashPassword(Password);

        Assert.NotEqual(Password, hash);
        var decoded = Convert.FromBase64String(hash);
        Assert.Equal(IdentityV3FormatMarker, decoded[0]);
    }

    [Fact]
    public void HashPassword_SamePasswordTwice_ReturnsDifferentHashes()
    {
        Assert.NotEqual(_hasher.HashPassword(Password), _hasher.HashPassword(Password));
    }

    // --- AC2 ---

    [Fact]
    public void VerifyHashedPassword_V3HashAndRightPassword_ReturnsSuccess()
    {
        var hash = _hasher.HashPassword(Password);

        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(hash, Password));
    }

    [Fact]
    public void VerifyHashedPassword_V3HashAndWrongPassword_ReturnsFailed()
    {
        var hash = _hasher.HashPassword(Password);

        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(hash, WrongPassword));
    }

    // --- AC3 ---

    [Fact]
    public void VerifyHashedPassword_LegacyHashAndRightPassword_ReturnsSuccessRehashNeeded()
    {
        Assert.Equal(
            PasswordVerificationResult.SuccessRehashNeeded,
            _hasher.VerifyHashedPassword(LegacyHash(Password), Password));
    }

    [Fact]
    public void VerifyHashedPassword_LegacyHashAndWrongPassword_ReturnsFailed()
    {
        Assert.Equal(
            PasswordVerificationResult.Failed,
            _hasher.VerifyHashedPassword(LegacyHash(Password), WrongPassword));
    }

    // --- AC4 ---

    [Fact]
    public void VerifyHashedPassword_V3HashWithFewerIterationsThanConfigured_ReturnsSuccessRehashNeeded()
    {
        var weakerHasher = HasherWith(new PasswordHasherOptions { IterationCount = WeakerIterationCount });
        var weakerHash = weakerHasher.HashPassword(Password);

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, _hasher.VerifyHashedPassword(weakerHash, Password));
    }

    [Fact]
    public void VerifyHashedPassword_V3HashWithFewerIterationsAndWrongPassword_ReturnsFailed()
    {
        var weakerHash = HasherWith(new PasswordHasherOptions { IterationCount = WeakerIterationCount }).HashPassword(Password);

        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(weakerHash, WrongPassword));
    }

    // --- AC5 ---

    public static TheoryData<string> MalformedHashes() =>
    [
        string.Empty,
        "   ",
        "not base64 at all!",
        "%%%%",
        "abc",
        Convert.ToBase64String(ArbitraryBytes(1)),
        Convert.ToBase64String(ArbitraryBytes(16)),
        Convert.ToBase64String(ArbitraryBytes(31)),
        Convert.ToBase64String(ArbitraryBytes(33)),
        Convert.ToBase64String(ArbitraryBytes(64)),
        Convert.ToBase64String(WithMarker(0x00, 10)),
        Convert.ToBase64String(WithMarker(0x00, 49)),
        Convert.ToBase64String(WithMarker(IdentityV3FormatMarker, 5)),
        Convert.ToBase64String(WithMarker(IdentityV3FormatMarker, 61)),
        Convert.ToBase64String(WithMarker(0x7F, 61))
    ];

    [Theory]
    [MemberData(nameof(MalformedHashes))]
    public void VerifyHashedPassword_MalformedStoredValue_ReturnsFailedWithoutThrowing(string malformed)
    {
        var result = Record.Exception(() => _hasher.VerifyHashedPassword(malformed, Password));

        Assert.Null(result);
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(malformed, Password));
    }

    // A real V3 hash cut after its header and salt, so it carries no derived key at all
    [Fact]
    public void VerifyHashedPassword_V3HashWithoutItsDerivedKey_ReturnsFailedWithoutThrowing()
    {
        var decoded = Convert.FromBase64String(_hasher.HashPassword(Password));
        var truncated = Convert.ToBase64String(decoded.AsSpan(0, V3HeaderAndSaltLength).ToArray());

        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(truncated, Password));
    }

    // --- AC6 (source test) ---

    [Fact]
    public void PasswordHasherSource_ComparesHashes_OnlyInConstantTime()
    {
        var code = LineComment.Replace(SolutionFiles.ReadText(HasherSourceFile), string.Empty);

        Assert.Contains(FixedTimeEquals, code);
        Assert.All(NonConstantTimeComparisons, comparison => Assert.DoesNotContain(comparison, code));
        Assert.All(EqualityOperands.Matches(code), match =>
        {
            Assert.Matches(SizeOrLiteral, match.Groups["left"].Value);
            Assert.Matches(SizeOrLiteral, match.Groups["right"].Value);
        });
    }

    private static InfrastructurePasswordHasher HasherWith(PasswordHasherOptions options) =>
        new(Options.Create(options));

    // The unsalted SHA-256 hash stored before #167
    private static string LegacyHash(string password) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    // Arbitrary but fixed bytes, so a run never depends on chance (random bytes could, rarely, read as a V3
    // header with a huge iteration count)
    private static byte[] ArbitraryBytes(int length) =>
        Enumerable.Range(0, length).Select(i => (byte)(i * ArbitraryByteStep + ArbitraryByteOffset)).ToArray();

    private static byte[] WithMarker(byte marker, int length)
    {
        var bytes = ArbitraryBytes(length);
        bytes[0] = marker;
        return bytes;
    }
}

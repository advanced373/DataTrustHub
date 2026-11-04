using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataTrustHub.Features.Tests.Configuration;

// #172 PR 4 (trusted client address): Registration:TrustedClientKey is optional; a set key shorter than 32
// characters stops the host at startup with a message that names the setting (AC5: never the key itself)
public class TrustedClientKeyStartupTests
{
    private const string SettingName = "TrustedClientKey";
    private const int MinimumLength = 32;

    // Distinct characters, so a leaked key (or a long part of it) is found in a message
    private const string KeyCharacters = "Qx7Rk2Lm9Pz4Wv8Tn3Hs6Jd1Fg5Bc0YaUeIoMN";

    public static TheoryData<int> TooShortLengths => new() { 1, 8, MinimumLength - 1 };

    // Long enough that finding the key in a message is not chance
    public static TheoryData<int> TooShortDistinctiveLengths => new() { 8, MinimumLength - 1 };

    public static TheoryData<int> AcceptedLengths => new() { MinimumLength, MinimumLength + 12, 64 };

    [Theory]
    [MemberData(nameof(TooShortLengths))]
    public void StartupValidation_KeyShorterThan32_FailsNamingTheSetting(int length)
    {
        using var services = FeaturesWith(Key(length));

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(SettingName, exception.Message);
    }

    // AC5: the validation message never echoes the key
    [Theory]
    [MemberData(nameof(TooShortDistinctiveLengths))]
    public void StartupValidation_KeyShorterThan32_NeverEchoesTheKey(int length)
    {
        var key = Key(length);
        using var services = FeaturesWith(key);

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.DoesNotContain(key, exception.ToString());
        Assert.All(exception.Failures, failure => Assert.DoesNotContain(key, failure));
    }

    [Theory]
    [MemberData(nameof(AcceptedLengths))]
    public void StartupValidation_KeyOf32OrMore_Passes(int length)
    {
        using var services = FeaturesWith(Key(length));

        services.GetRequiredService<IStartupValidator>().Validate();
    }

    // Empty or missing: no client address header is trusted, and the host starts
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void StartupValidation_NoKey_Passes(string? key)
    {
        using var services = FeaturesWith(key);

        services.GetRequiredService<IStartupValidator>().Validate();
    }

    // The host itself refuses to start, and what it throws does not carry the key
    [Fact]
    public async Task HostStart_KeyShorterThan32_FailsWithoutTheKey()
    {
        var key = Key(MinimumLength - 1);
        var factory = new TrustedClientApiFactory(new Dictionary<string, string?> { [TrustedClientApiFactory.TrustedClientKeySetting] = key });

        var exception = await Record.ExceptionAsync(() => RegistrationApiFactory.StartFactoryAsync(factory));

        Assert.NotNull(exception);
        Assert.DoesNotContain(key, exception.ToString());
        Assert.False(factory.Logs.Contain(key), "The too short key was logged.");
    }

    [Fact]
    public async Task HostStart_KeyOf32_Starts()
    {
        var key = Key(MinimumLength);
        var factory = await TrustedClientApiFactory.StartAsync(key, permits: 5);
        try
        {
            Assert.False(factory.Logs.Contain(key), "The key was logged at startup.");
        }
        finally
        {
            await factory.StopAsync();
        }
    }

    [Fact]
    public async Task HostStart_EmptyKey_Starts()
    {
        var factory = await TrustedClientApiFactory.StartAsync(trustedClientKey: null, permits: 5);

        await factory.StopAsync();
    }

    // --- helpers ---

    private static string Key(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(i => KeyCharacters[i % KeyCharacters.Length]));

    // The API's own feature registrations with the given key (plus a test signing key, which they require)
    private static ServiceProvider FeaturesWith(string? key)
    {
        var settings = new Dictionary<string, string?> { [TestJwt.SecretKey] = TestJwt.SigningKey };
        if (key is not null) settings[TrustedClientApiFactory.TrustedClientKeySetting] = key;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddLogging().AddFeatures(configuration).BuildServiceProvider();
    }
}

using System.Text.Json;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Registration;
using DataTrustHub.Features.Tests.Authentication.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataTrustHub.Features.Tests.Configuration;

// #172 AC7: the three registration settings are read from the Registration section of the configuration, the
// committed values equal the class defaults, and a value below 1 stops the host when it starts
public class RegistrationOptionsStartupTests
{
    private const string SolutionFile = "DataTrustHub.sln";
    private const string ApiProjectFolder = "DataTrustHub.API";
    private const string ApiSettingsFile = "appsettings.json";

    // The values the spec gives (and appsettings.json commits)
    private const int SpecifiedLifetimeDays = 14;
    private const int SpecifiedPermits = 5;
    private const int SpecifiedMaxPending = 100;

    private const int ConfiguredLifetimeDays = 7;
    private const int ConfiguredPermits = 9;
    private const int ConfiguredMaxPending = 11;

    public static TheoryData<string, string> SettingsBelowOne => new()
    {
        { RegistrationApiFactory.LifetimeDaysSetting, "0" },
        { RegistrationApiFactory.PermitsPerHourPerIpSetting, "0" },
        { RegistrationApiFactory.MaxPendingRequestsSetting, "0" },
        { RegistrationApiFactory.LifetimeDaysSetting, "-1" },
        { RegistrationApiFactory.PermitsPerHourPerIpSetting, "-5" },
        { RegistrationApiFactory.MaxPendingRequestsSetting, "-100" }
    };

    // The check the host runs when it starts (ValidateOnStart registers it as the IStartupValidator), on the
    // services the API registers. Starting a whole test host that fails is racy in WebApplicationFactory (the
    // failure sometimes surfaces as ObjectDisposedException), so the startup validation is run directly
    [Theory]
    [MemberData(nameof(SettingsBelowOne))]
    public void StartupValidation_SettingBelowOne_FailsNamingTheSetting(string setting, string value)
    {
        using var services = FeaturesWith(new Dictionary<string, string?> { [setting] = value });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(PropertyName(setting), exception.Message);
    }

    [Fact]
    public void StartupValidation_LifetimeAboveTheMaximum_FailsNamingTheSetting()
    {
        var tooLong = (RegistrationOptions.MaxPendingRequestLifetimeDays + 1).ToString();
        using var services = FeaturesWith(new Dictionary<string, string?> { [RegistrationApiFactory.LifetimeDaysSetting] = tooLong });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(PropertyName(RegistrationApiFactory.LifetimeDaysSetting), exception.Message);
    }

    [Fact]
    public void StartupValidation_LifetimeAtTheMaximum_Passes()
    {
        var atMax = RegistrationOptions.MaxPendingRequestLifetimeDays.ToString();
        using var services = FeaturesWith(new Dictionary<string, string?> { [RegistrationApiFactory.LifetimeDaysSetting] = atMax });

        services.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void StartupValidation_CommittedSettings_Pass()
    {
        using var services = FeaturesWith(new Dictionary<string, string?>
        {
            [RegistrationApiFactory.LifetimeDaysSetting] = RegistrationOptions.DefaultPendingRequestLifetimeDays.ToString(),
            [RegistrationApiFactory.PermitsPerHourPerIpSetting] = RegistrationOptions.DefaultPermitsPerHourPerIp.ToString(),
            [RegistrationApiFactory.MaxPendingRequestsSetting] = RegistrationOptions.DefaultMaxPendingRequests.ToString()
        });

        services.GetRequiredService<IStartupValidator>().Validate();
    }

    // The host itself refuses to start: whatever the race above, starting with a value below 1 never succeeds
    [Fact]
    public async Task HostStart_SettingBelowOne_Fails()
    {
        var factory = new RegistrationApiFactory(new Dictionary<string, string?> { [RegistrationApiFactory.MaxPendingRequestsSetting] = "0" });

        Assert.NotNull(await Record.ExceptionAsync(() => RegistrationApiFactory.StartFactoryAsync(factory)));
    }

    [Fact]
    public async Task HostStart_EverySettingAtOne_Starts()
    {
        var factory = await RegistrationApiFactory.StartAsync(new Dictionary<string, string?>
        {
            [RegistrationApiFactory.LifetimeDaysSetting] = "1",
            [RegistrationApiFactory.PermitsPerHourPerIpSetting] = "1",
            [RegistrationApiFactory.MaxPendingRequestsSetting] = "1"
        });

        await factory.StopAsync();
    }

    [Fact]
    public async Task Options_AreReadFromTheRegistrationSection()
    {
        var factory = await RegistrationApiFactory.StartAsync(new Dictionary<string, string?>
        {
            [RegistrationApiFactory.LifetimeDaysSetting] = ConfiguredLifetimeDays.ToString(),
            [RegistrationApiFactory.PermitsPerHourPerIpSetting] = ConfiguredPermits.ToString(),
            [RegistrationApiFactory.MaxPendingRequestsSetting] = ConfiguredMaxPending.ToString()
        });
        try
        {
            var options = factory.Services.GetRequiredService<IOptions<RegistrationOptions>>().Value;

            Assert.Equal(
                (ConfiguredLifetimeDays, ConfiguredPermits, ConfiguredMaxPending),
                (options.PendingRequestLifetimeDays, options.PermitsPerHourPerIp, options.MaxPendingRequests));
        }
        finally
        {
            await factory.StopAsync();
        }
    }

    // Without overrides the host reads the committed appsettings.json (the lifted test permit aside)
    [Fact]
    public async Task Options_WithoutOverrides_AreTheCommittedDefaults()
    {
        var factory = await RegistrationApiFactory.StartAsync(new Dictionary<string, string?>
        {
            [RegistrationApiFactory.PermitsPerHourPerIpSetting] = null
        });
        try
        {
            var options = factory.Services.GetRequiredService<IOptions<RegistrationOptions>>().Value;

            Assert.Equal(
                (SpecifiedLifetimeDays, SpecifiedPermits, SpecifiedMaxPending),
                (options.PendingRequestLifetimeDays, options.PermitsPerHourPerIp, options.MaxPendingRequests));
        }
        finally
        {
            await factory.StopAsync();
        }
    }

    [Fact]
    public void ApiSettings_RegistrationSection_StatesTheClassDefaults()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), ApiProjectFolder, ApiSettingsFile)));
        var section = settings.RootElement.GetProperty(RegistrationOptions.SectionName);

        Assert.Equal(
            (RegistrationOptions.DefaultPendingRequestLifetimeDays, RegistrationOptions.DefaultPermitsPerHourPerIp, RegistrationOptions.DefaultMaxPendingRequests),
            (section.GetProperty(nameof(RegistrationOptions.PendingRequestLifetimeDays)).GetInt32(),
             section.GetProperty(nameof(RegistrationOptions.PermitsPerHourPerIp)).GetInt32(),
             section.GetProperty(nameof(RegistrationOptions.MaxPendingRequests)).GetInt32()));
    }

    [Fact]
    public void RegistrationOptions_ClassDefaults_AreTheSpecifiedValues()
    {
        var options = new RegistrationOptions();

        Assert.Equal((SpecifiedLifetimeDays, SpecifiedPermits, SpecifiedMaxPending), (options.PendingRequestLifetimeDays, options.PermitsPerHourPerIp, options.MaxPendingRequests));
    }

    // --- helpers ---

    private static string PropertyName(string setting) => setting[(setting.IndexOf(':') + 1)..];

    // The API's own feature registrations on the given configuration (plus a test signing key, which they require)
    private static ServiceProvider FeaturesWith(IDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(settings) { [TestJwt.SecretKey] = TestJwt.SigningKey })
            .Build();
        return new ServiceCollection().AddLogging().AddFeatures(configuration).BuildServiceProvider();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException($"{SolutionFile} not found above {AppContext.BaseDirectory}");
    }
}

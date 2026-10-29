namespace DataTrustHub.Features.Tests.AzureSetup.Helpers;

/// <summary>
/// The Azure state the fake az answers with during a dry run of the setup script.
/// </summary>
/// <param name="SignedIn">Whether az account show succeeds.</param>
/// <param name="AppSettingsReadFails">Whether every az webapp config appsettings list exits with an error.</param>
/// <param name="ApiSettingNames">The app setting names the fake reports (for any app).</param>
internal sealed record DryRunScenario(bool SignedIn, bool AppSettingsReadFails, IReadOnlyList<string> ApiSettingNames)
{
    internal const string ConnectionStringSetting = "ConnectionStrings__DefaultConnection";
    internal const string JwtSecretSetting = "Jwt__Secret";
    internal const string AdminSeedEmailSetting = "AdminSeed__Email";
    internal const string AdminSeedPasswordSetting = "AdminSeed__Password";

    // The three Application Insights settings the portal created on each web app
    internal static readonly IReadOnlyList<string> ApplicationInsightsSettings =
    [
        "APPLICATIONINSIGHTS_CONNECTION_STRING",
        "ApplicationInsightsAgent_EXTENSION_VERSION",
        "XDT_MicrosoftApplicationInsights_Mode",
    ];

    internal static DryRunScenario SignedOut { get; } = new(false, false, []);

    /// <summary>Signed in, the API has only the Application Insights settings.</summary>
    internal static DryRunScenario NothingSetYet { get; } = new(true, false, ApplicationInsightsSettings);

    /// <summary>Signed in, the API already has its JWT signing key and admin seed.</summary>
    internal static DryRunScenario SecretsAlreadySet { get; } = new(true, false,
        [.. ApplicationInsightsSettings, JwtSecretSetting, AdminSeedEmailSetting, AdminSeedPasswordSetting]);

    /// <summary>Signed in, but reading the app settings fails (permissions, throttling, network).</summary>
    internal static DryRunScenario AppSettingsUnreadable { get; } = new(true, true, []);
}

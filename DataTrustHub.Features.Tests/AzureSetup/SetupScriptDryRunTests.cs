using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.AzureSetup.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.AzureSetup;

// #101 AC1 (Jwt__Secret generated only if missing, -RotateJwtSecret, Api__BaseUrl read from Azure) and AC2
// (dry run: prints the change commands with secrets masked, changes nothing, uses placeholders when signed
// out); #147 AC1-AC3 (the admin seed is planned only with -SeedAdmin, masked, overwriting existing values,
// ADR-0022). infra/setup.ps1 runs only with -WhatIf, against a fake az; the real Azure CLI is never on PATH
// (see SetupScriptDryRun).
public class SetupScriptDryRunTests
{
    private const string AppSettingsSetCommand = "webapp config appsettings set";
    private const string AppSettingsDeleteCommand = "webapp config appsettings delete";
    private const string ManagedIdentityAuthentication = "Authentication=Active Directory Managed Identity";
    private const string AdminSeedPrompt = "Admin seed";
    private const string MaskedValue = "***";
    private const string ApiHostPlaceholder = "<API default host name>";
    private const string AccountShowCommand = "account show";
    private const string ApiBaseUrlSetting = "Api__BaseUrl";

    // An az verb that changes Azure; a dry run must never run one
    private static readonly Regex ChangeVerb = new(@"\b(create|set|assign|update|delete|remove)\b");

    // A printed setting: "      Name=value"
    private static Regex SettingLine(string settingName) =>
        new($@"^\s*{Regex.Escape(settingName)}=(?<value>.*)$", RegexOptions.Multiline);

    [Fact]
    public void DryRun_SignedOut_ExitsZero()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SignedOut, SetupScriptDryRun.WhatIfSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public void DryRun_SignedOut_RunsOnlyTheSignInCheck()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SignedOut, SetupScriptDryRun.WhatIfSwitch);

        var call = Assert.Single(result.AzCalls);
        Assert.StartsWith(AccountShowCommand, call);
    }

    [Fact]
    public void DryRun_SignedOut_PlansApiBaseUrlWithHostNamePlaceholder()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SignedOut, SetupScriptDryRun.WhatIfSwitch);

        var value = SettingValue(result, ApiBaseUrlSetting);
        Assert.Contains(ApiHostPlaceholder, value);
    }

    [Fact]
    public void DryRun_SignedOut_PlansJwtSecretMasked()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SignedOut, SetupScriptDryRun.WhatIfSwitch);

        Assert.Equal(MaskedValue, SettingValue(result, DryRunScenario.JwtSecretSetting));
    }

    // #147 AC1: without -SeedAdmin the admin seed is never planned, even when the dry run assumes nothing is set
    [Theory]
    [InlineData(DryRunScenario.AdminSeedEmailSetting)]
    [InlineData(DryRunScenario.AdminSeedPasswordSetting)]
    public void DryRun_SignedOut_DoesNotPlanAdminSeed(string settingName)
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SignedOut, SetupScriptDryRun.WhatIfSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(settingName, result.Output);
    }

    // #147 AC2: -SeedAdmin plans both settings masked and, with stdin closed and -NonInteractive, asks nothing
    [Theory]
    [InlineData(DryRunScenario.AdminSeedEmailSetting)]
    [InlineData(DryRunScenario.AdminSeedPasswordSetting)]
    public void DryRun_SignedOutAndSeedAdmin_PlansAdminSeedMasked(string settingName)
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SignedOut,
            SetupScriptDryRun.WhatIfSwitch, SetupScriptDryRun.SeedAdminSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(MaskedValue, SettingValue(result, settingName));
    }

    [Fact]
    public void DryRun_SignedIn_RunsNoAzCommandThatChangesAzure()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.NothingSetYet, SetupScriptDryRun.WhatIfSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.NotEmpty(result.AzCalls);
        Assert.DoesNotContain(result.AzCalls, call => ChangeVerb.IsMatch(call));
    }

    [Fact]
    public void DryRun_SignedIn_PlansApiBaseUrlFromApiDefaultHostName()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.NothingSetYet, SetupScriptDryRun.WhatIfSwitch);

        Assert.Equal($"https://{SetupScriptDryRun.FakeApiHostName}", SettingValue(result, ApiBaseUrlSetting));
    }

    [Fact]
    public void DryRun_JwtSecretMissing_PlansMaskedJwtSecret()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.NothingSetYet, SetupScriptDryRun.WhatIfSwitch);

        Assert.Equal(MaskedValue, SettingValue(result, DryRunScenario.JwtSecretSetting));
    }

    [Fact]
    public void DryRun_JwtSecretExists_DoesNotPlanJwtSecret()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SecretsAlreadySet, SetupScriptDryRun.WhatIfSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(DryRunScenario.JwtSecretSetting, result.Output);
    }

    [Fact]
    public void DryRun_JwtSecretExistsAndRotateJwtSecret_PlansMaskedJwtSecret()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SecretsAlreadySet,
            SetupScriptDryRun.WhatIfSwitch, SetupScriptDryRun.RotateJwtSecretSwitch);

        Assert.Equal(MaskedValue, SettingValue(result, DryRunScenario.JwtSecretSetting));
    }

    // #147 AC1: an existing admin seed is neither planned again nor deleted
    [Theory]
    [InlineData(DryRunScenario.AdminSeedEmailSetting)]
    [InlineData(DryRunScenario.AdminSeedPasswordSetting)]
    public void DryRun_AdminSeedExists_DoesNotPlanAdminSeed(string settingName)
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SecretsAlreadySet, SetupScriptDryRun.WhatIfSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(settingName, result.Output);
    }

    [Fact]
    public void DryRun_AdminSeedExists_PlansNoAppSettingsDelete()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SecretsAlreadySet, SetupScriptDryRun.WhatIfSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(AppSettingsDeleteCommand, result.Output);
    }

    // #147 AC1: a missing admin seed is not planned either (the old contract planned it)
    [Theory]
    [InlineData(DryRunScenario.AdminSeedEmailSetting)]
    [InlineData(DryRunScenario.AdminSeedPasswordSetting)]
    public void DryRun_AdminSeedMissing_DoesNotPlanAdminSeed(string settingName)
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.NothingSetYet, SetupScriptDryRun.WhatIfSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(settingName, result.Output);
    }

    // #147 AC1: the rest of the plan is unchanged when the admin seed is missing
    [Fact]
    public void DryRun_AdminSeedMissing_StillPlansJwtSecretAndConnectionString()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.NothingSetYet, SetupScriptDryRun.WhatIfSwitch);

        Assert.Equal(MaskedValue, SettingValue(result, DryRunScenario.JwtSecretSetting));
        Assert.Contains(ManagedIdentityAuthentication, SettingValue(result, DryRunScenario.ConnectionStringSetting));
    }

    // #147 AC2: -SeedAdmin writes both settings even when they are missing
    [Theory]
    [InlineData(DryRunScenario.AdminSeedEmailSetting)]
    [InlineData(DryRunScenario.AdminSeedPasswordSetting)]
    public void DryRun_AdminSeedMissingAndSeedAdmin_PlansAdminSeedMasked(string settingName)
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.NothingSetYet,
            SetupScriptDryRun.WhatIfSwitch, SetupScriptDryRun.SeedAdminSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(MaskedValue, SettingValue(result, settingName));
    }

    // #147 AC2: -SeedAdmin overwrites an existing admin seed
    [Theory]
    [InlineData(DryRunScenario.AdminSeedEmailSetting)]
    [InlineData(DryRunScenario.AdminSeedPasswordSetting)]
    public void DryRun_AdminSeedExistsAndSeedAdmin_PlansAdminSeedMasked(string settingName)
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SecretsAlreadySet,
            SetupScriptDryRun.WhatIfSwitch, SetupScriptDryRun.SeedAdminSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(MaskedValue, SettingValue(result, settingName));
    }

    // #147 AC2: -SeedAdmin does not touch the JWT signing key, which exists
    [Fact]
    public void DryRun_AdminSeedExistsAndSeedAdmin_DoesNotPlanJwtSecret()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SecretsAlreadySet,
            SetupScriptDryRun.WhatIfSwitch, SetupScriptDryRun.SeedAdminSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(DryRunScenario.JwtSecretSetting, result.Output);
    }

    [Fact]
    public void DryRun_SignedInAndSeedAdmin_RunsNoAzCommandThatChangesAzure()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SecretsAlreadySet,
            SetupScriptDryRun.WhatIfSwitch, SetupScriptDryRun.SeedAdminSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.NotEmpty(result.AzCalls);
        Assert.DoesNotContain(result.AzCalls, call => ChangeVerb.IsMatch(call));
    }

    // #147 AC3: the admin seed lines carry only the mask, and no prompt text reaches the output
    [Fact]
    public void DryRun_SeedAdmin_PrintsNoAdminSeedPrompt()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.NothingSetYet,
            SetupScriptDryRun.WhatIfSwitch, SetupScriptDryRun.SeedAdminSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(AdminSeedPrompt, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DryRun_AppSettingsReadFailsAndSeedAdmin_PlansNoAppSettingsSet()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.AppSettingsUnreadable,
            SetupScriptDryRun.WhatIfSwitch, SetupScriptDryRun.SeedAdminSwitch);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(AppSettingsSetCommand, result.Output);
        Assert.DoesNotContain(DryRunScenario.AdminSeedEmailSetting + "=", result.Output);
    }

    // The connection string is not a secret (managed identity, no password): it is still planned
    [Fact]
    public void DryRun_SecretsExist_StillPlansConnectionString()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SecretsAlreadySet, SetupScriptDryRun.WhatIfSwitch);

        Assert.Contains(ManagedIdentityAuthentication, SettingValue(result, DryRunScenario.ConnectionStringSetting));
    }

    [Fact]
    public void DryRun_AppSettingsReadFails_ExitsNonZero()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.AppSettingsUnreadable, SetupScriptDryRun.WhatIfSwitch);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void DryRun_AppSettingsReadFails_PlansNoAppSettingsSet()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.AppSettingsUnreadable, SetupScriptDryRun.WhatIfSwitch);

        Assert.DoesNotContain(AppSettingsSetCommand, result.Output);
        Assert.DoesNotContain(DryRunScenario.JwtSecretSetting + "=", result.Output);
    }

    [Fact]
    public void DryRun_AppSettingsReadFailsAndRotateJwtSecret_PlansNoAppSettingsSet()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.AppSettingsUnreadable,
            SetupScriptDryRun.WhatIfSwitch, SetupScriptDryRun.RotateJwtSecretSwitch);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(AppSettingsSetCommand, result.Output);
    }

    private static string SettingValue(DryRunResult result, string settingName)
    {
        var match = SettingLine(settingName).Match(result.Output);
        Assert.True(match.Success, $"{settingName} is not in the plan:{Environment.NewLine}{result.Output}");
        return match.Groups["value"].Value.TrimEnd('\r');
    }
}

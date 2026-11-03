using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.AzureSetup.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.AzureSetup;

// #172 PR 4, AC6 and the setup side of AC5: infra/setup.ps1 writes one new client key to both apps (API
// Registration__TrustedClientKey, WebApp Api__ClientKey) when either lacks it or with -RotateClientKey, leaves
// both untouched when both hold it, masks it in a dry run and never prints it. Its value is never read back.
// Runs against the stateful fake az; the real Azure CLI is never on PATH.
public class SetupScriptClientKeyTests
{
    private const string RotateClientKeySwitch = "-RotateClientKey";
    private const string MaskedValue = "***";
    private const string Api = FakeAzure.ApiAppName;
    private const string WebApp = FakeAzure.WebAppName;
    private const string ApiKey = FakeAzure.ApiClientKeySetting;
    private const string WebAppKey = FakeAzure.WebAppClientKeySetting;

    // The shortest key the API accepts at startup
    private const int MinimumKeyLength = 32;

    // A printed setting of a dry run: "      Name=value"
    private static Regex SettingLine(string settingName) =>
        new($@"^\s*{Regex.Escape(settingName)}=(?<value>.*?)\r?$", RegexOptions.Multiline);

    // Which app already holds the key before the run
    public enum Holder { Neither, ApiOnly, WebAppOnly }

    // --- real runs: a new key, the same on both apps ---

    [Theory]
    [InlineData(Holder.Neither)]
    [InlineData(Holder.ApiOnly)]
    [InlineData(Holder.WebAppOnly)]
    public void RealRun_EitherAppLacksTheKey_WritesTheSameKeyToBoth(Holder holder)
    {
        var result = SetupScriptRun.RealRun(StateWith(holder));

        Assert.True(result.ExitCode == 0, result.Output);
        var apiKey = result.FinalState.Received(ApiKey);
        var webAppKey = result.FinalState.Received(WebAppKey);
        Assert.False(string.IsNullOrEmpty(apiKey), "The script sent no client key to the API.");
        Assert.Equal(apiKey, webAppKey);
        Assert.Equal(apiKey, StoredValue(result.FinalState, Api, ApiKey));
        Assert.Equal(apiKey, StoredValue(result.FinalState, WebApp, WebAppKey));
    }

    // A key held by one app is never copied (its value is not read): both get a new one
    [Theory]
    [InlineData(Holder.ApiOnly)]
    [InlineData(Holder.WebAppOnly)]
    public void RealRun_OneAppHoldsTheKey_WritesANewKey(Holder holder)
    {
        var result = SetupScriptRun.RealRun(StateWith(holder));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.NotEqual(FakeAzure.ExistingClientKey, result.FinalState.Received(ApiKey));
        Assert.DoesNotContain(result.AzCalls, call => call.Contains($"[?name=='{ApiKey}'].value") || call.Contains($"[?name=='{WebAppKey}'].value"));
    }

    // The key the API will accept: at least 32 characters, random from run to run
    [Fact]
    public void RealRun_NewKey_IsLongEnoughAndRandom()
    {
        var first = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(FakeAzure.BasicTier));
        var second = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(FakeAzure.BasicTier));

        var firstKey = first.FinalState.Received(ApiKey)!;
        Assert.True(firstKey.Length >= MinimumKeyLength, $"The key has {firstKey.Length} characters.");
        Assert.NotEqual(firstKey, second.FinalState.Received(ApiKey));
    }

    [Fact]
    public void RealRun_BothAppsHoldTheKey_LeavesBothUntouched()
    {
        var result = SetupScriptRun.RealRun(FakeAzure.FullyConfigured(FakeAzure.BasicTier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Null(result.FinalState.Received(ApiKey));
        Assert.Null(result.FinalState.Received(WebAppKey));
        Assert.Equal(FakeAzure.ExistingClientKey, StoredValue(result.FinalState, Api, ApiKey));
        Assert.Equal(FakeAzure.ExistingClientKey, StoredValue(result.FinalState, WebApp, WebAppKey));
    }

    [Fact]
    public void RealRun_BothAppsHoldTheKeyAndRotateClientKey_WritesTheSameNewKeyToBoth()
    {
        var result = SetupScriptRun.RealRun(FakeAzure.FullyConfigured(FakeAzure.BasicTier), RotateClientKeySwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        var apiKey = result.FinalState.Received(ApiKey);
        Assert.False(string.IsNullOrEmpty(apiKey), "The script sent no client key to the API.");
        Assert.NotEqual(FakeAzure.ExistingClientKey, apiKey);
        Assert.Equal(apiKey, result.FinalState.Received(WebAppKey));
        Assert.Equal(apiKey, StoredValue(result.FinalState, WebApp, WebAppKey));
    }

    // Rotating the client key does not rotate the JWT signing key
    [Fact]
    public void RealRun_RotateClientKey_LeavesTheJwtSecretUntouched()
    {
        var result = SetupScriptRun.RealRun(FakeAzure.FullyConfigured(FakeAzure.BasicTier), RotateClientKeySwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Null(result.FinalState.Received(DryRunScenario.JwtSecretSetting));
    }

    // Read-back checks existence only: the key's value is never read back, even when Azure stores another one
    [Fact]
    public void RealRun_ClientKeyWritten_ChecksItsNameNotItsValue()
    {
        var azure = StateWith(Holder.Neither);
        azure.IgnoredChanges.Add(FakeChange.AppSettingValues);

        var result = SetupScriptRun.RealRun(azure);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(result.FinalState.Apps[Api].Settings, setting => setting.Name == ApiKey);
        Assert.Contains(result.FinalState.Apps[WebApp].Settings, setting => setting.Name == WebAppKey);
        Assert.DoesNotContain(result.AzCalls, call => call.Contains($"[?name=='{ApiKey}'].value") || call.Contains($"[?name=='{WebAppKey}'].value"));
    }

    // --- AC5: the key is never printed ---

    [Theory]
    [InlineData(Holder.Neither, false)]
    [InlineData(Holder.ApiOnly, false)]
    [InlineData(Holder.Neither, true)]
    public void RealRun_ClientKeyWritten_NeverPrintsIt(Holder holder, bool rotate)
    {
        var result = rotate
            ? SetupScriptRun.RealRun(FakeAzure.FullyConfigured(FakeAzure.BasicTier), RotateClientKeySwitch)
            : SetupScriptRun.RealRun(StateWith(holder));

        Assert.True(result.ExitCode == 0, result.Output);
        var key = result.FinalState.Received(ApiKey);
        Assert.False(string.IsNullOrEmpty(key), "The script sent no client key.");
        Assert.DoesNotContain(key, result.Output);
        Assert.False(result.OutputContains(key), "The client key was printed, cut over two lines.");
    }

    // Not even the read-back error prints it
    [Theory]
    [InlineData(FakeChange.AppSettings)]
    [InlineData(FakeChange.AppSettingValues)]
    public void RealRun_AppSettingsNotApplied_ErrorDoesNotPrintTheKey(string ignoredChange)
    {
        var azure = StateWith(Holder.Neither);
        azure.IgnoredChanges.Add(ignoredChange);

        var result = SetupScriptRun.RealRun(azure);

        var key = result.FinalState.Received(ApiKey);
        Assert.False(string.IsNullOrEmpty(key), "The script sent no client key.");
        Assert.DoesNotContain(key, result.Output);
        Assert.False(result.OutputContains(key), "The client key was printed, cut over two lines.");
    }

    // The existing key is never read, so never printed either, even when it is rotated
    [Fact]
    public void RealRun_RotateClientKey_NeverPrintsTheExistingKey()
    {
        var result = SetupScriptRun.RealRun(FakeAzure.FullyConfigured(FakeAzure.BasicTier), RotateClientKeySwitch);

        Assert.DoesNotContain(FakeAzure.ExistingClientKey, result.Output);
    }

    // --- dry runs: masked, nothing written ---

    [Theory]
    [InlineData(Holder.Neither)]
    [InlineData(Holder.ApiOnly)]
    [InlineData(Holder.WebAppOnly)]
    public void DryRun_EitherAppLacksTheKey_PlansBothMasked(Holder holder)
    {
        var result = SetupScriptRun.DryRun(StateWith(holder));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(MaskedValue, PlannedValue(result, ApiKey));
        Assert.Equal(MaskedValue, PlannedValue(result, WebAppKey));
        Assert.Empty(result.FinalState.ReceivedSettings);
    }

    [Fact]
    public void DryRun_BothAppsHoldTheKeyAndRotateClientKey_PlansBothMasked()
    {
        var result = SetupScriptRun.DryRun(FakeAzure.FullyConfigured(FakeAzure.BasicTier), RotateClientKeySwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(MaskedValue, PlannedValue(result, ApiKey));
        Assert.Equal(MaskedValue, PlannedValue(result, WebAppKey));
        Assert.Empty(result.FinalState.ReceivedSettings);
        Assert.DoesNotContain(FakeAzure.ExistingClientKey, result.Output);
    }

    [Fact]
    public void DryRun_BothAppsHoldTheKey_PlansNeither()
    {
        var result = SetupScriptRun.DryRun(FakeAzure.FullyConfigured(FakeAzure.BasicTier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.False(SettingLine(ApiKey).IsMatch(result.Output), result.Output);
        Assert.False(SettingLine(WebAppKey).IsMatch(result.Output), result.Output);
    }

    // Signed out, nothing can be read: both are planned, masked
    [Fact]
    public void DryRun_SignedOut_PlansBothMasked()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SignedOut, SetupScriptDryRun.WhatIfSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(MaskedValue, PlannedValue(result.Output, ApiKey));
        Assert.Equal(MaskedValue, PlannedValue(result.Output, WebAppKey));
    }

    // --- helpers ---

    private static FakeAzure StateWith(Holder holder)
    {
        var azure = FakeAzure.FullyConfigured(FakeAzure.BasicTier);
        if (holder != Holder.ApiOnly) RemoveSetting(azure, Api, ApiKey);
        if (holder != Holder.WebAppOnly) RemoveSetting(azure, WebApp, WebAppKey);
        return azure;
    }

    private static void RemoveSetting(FakeAzure azure, string app, string name) =>
        azure.Apps[app].Settings.RemoveAll(setting => setting.Name == name);

    private static string? StoredValue(FakeAzure azure, string app, string name) =>
        azure.Apps[app].Settings.LastOrDefault(setting => setting.Name == name)?.Value;

    private static string PlannedValue(SetupRunResult result, string settingName) => PlannedValue(result.Output, settingName);

    private static string PlannedValue(string output, string settingName)
    {
        var match = SettingLine(settingName).Match(output);
        Assert.True(match.Success, $"{settingName} is not in the plan:{Environment.NewLine}{output}");
        return match.Groups["value"].Value;
    }
}

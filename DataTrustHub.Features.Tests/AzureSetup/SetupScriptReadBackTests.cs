using DataTrustHub.Features.Tests.AzureSetup.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.AzureSetup;

// #150 AC5: after every change command of a real run, infra/setup.ps1 reads the changed value back with the
// read the step used to decide, and stops with an error naming the step when Azure does not hold the target
// value (az can exit 0 and change nothing). Secret values are never printed, not even in that error.
// Real runs (-Force) against the stateful fake az; the real Azure CLI is never on PATH.
public class SetupScriptReadBackTests
{
    private const string Rg = "--resource-group DataTrustHub";
    private const string Api = FakeAzure.ApiAppName;
    private const string WebApp = FakeAzure.WebAppName;

    // One change step: what to ignore in the fake, the change call, the read that decides, and the words
    // the error must name the step with
    public static TheoryData<string, string, string, string> ChangeSteps => new()
    {
        { FakeChange.Identity, $"webapp identity assign {Rg} --name {Api} ",
            $"webapp show {Rg} --name {Api} --query identity.type", "managed identity" },
        { FakeChange.AppSettings, $"webapp config appsettings set {Rg} --name {Api} ",
            $"webapp config appsettings list {Rg} --name {Api} --query [].name", "app settings" },
        { FakeChange.Ftps, $"webapp config set {Rg} --name {Api} --ftps-state Disabled",
            $"webapp config show {Rg} --name {Api} --query ftpsState", "FTP" },
        { FakeChange.BasicAuth, "--parent sites/" + Api + " --name scm --set properties.allow=false",
            "resource show " + Rg + " --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies --parent sites/" + Api + " --name scm --query properties.allow",
            "basic publishing credentials" },
        { FakeChange.HealthCheck, $"webapp config set {Rg} --name {Api} --generic-configurations healthCheckPath=/health",
            $"webapp config show {Rg} --name {Api} --query healthCheckPath", "health check" },
        { FakeChange.Firewall, "sql server firewall-rule create " + Rg + " --server datatrusthub --name AllowAllWindowsAzureIps",
            $"sql server firewall-rule list {Rg} --server datatrusthub --query [].name", "firewall" },
    };

    // Every change sticks: the run succeeds and Azure holds the whole setup
    [Fact]
    public void RealRun_EveryChangeApplied_ExitsZeroWithSetupDone()
    {
        var result = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(FakeAzure.BasicTier));

        Assert.True(result.ExitCode == 0, result.Output);
        var api = result.FinalState.Apps[Api];
        Assert.Equal(FakeAzure.SystemAssigned, api.IdentityType);
        Assert.Contains(api.Settings, setting => setting.Name == DryRunScenario.JwtSecretSetting);
        Assert.Contains(result.FinalState.Apps[WebApp].Settings,
            setting => setting.Name == SetupScriptRun.ApiBaseUrlSetting && setting.Value == FakeAzure.ApiBaseUrl);
        Assert.Contains(FakeAzure.FirewallRuleName, result.FinalState.FirewallRules);
    }

    // After the change, the step's own read runs again
    [Theory]
    [MemberData(nameof(ChangeSteps))]
    public void RealRun_AfterChange_ReadsTheValueBackWithTheDecidingRead(
        string ignoredChange, string changeCall, string decidingRead, string stepWords)
    {
        _ = (ignoredChange, stepWords);
        var result = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(FakeAzure.BasicTier));

        Assert.True(result.ExitCode == 0, result.Output);
        var calls = result.AzCalls.ToList();
        var change = calls.FindIndex(call => call.Contains(changeCall));
        Assert.True(change >= 0, $"No change call '{changeCall}' in:{Environment.NewLine}{string.Join(Environment.NewLine, calls)}");
        Assert.Contains(calls.Take(change), call => call.StartsWith(decidingRead));
        Assert.Contains(calls.Skip(change + 1), call => call.StartsWith(decidingRead));
    }

    // az exits 0 but changes nothing: the script stops with an error naming the step
    [Theory]
    [MemberData(nameof(ChangeSteps))]
    public void RealRun_ChangeNotApplied_StopsNamingTheStep(
        string ignoredChange, string changeCall, string decidingRead, string stepWords)
    {
        _ = decidingRead;
        var azure = FakeAzure.NothingConfigured(FakeAzure.BasicTier);
        azure.IgnoredChanges.Add(ignoredChange);

        var result = SetupScriptRun.RealRun(azure);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.AzCalls, call => call.Contains(changeCall));
        Assert.Contains(stepWords, result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(stepWords.Equals("firewall") ? "datatrusthub" : Api, result.StandardError);
    }

    // The script stops at the failed step: no later change runs
    [Fact]
    public void RealRun_IdentityNotApplied_RunsNoLaterChange()
    {
        var azure = FakeAzure.NothingConfigured(FakeAzure.BasicTier);
        azure.IgnoredChanges.Add(FakeChange.Identity);

        var result = SetupScriptRun.RealRun(azure);

        Assert.NotEqual(0, result.ExitCode);
        var change = Assert.Single(result.ChangeCalls);
        Assert.StartsWith("webapp identity assign", change);
    }

    // A non-secret setting must hold the target value, not only exist
    [Fact]
    public void RealRun_NonSecretSettingValueDiffers_StopsNamingTheStep()
    {
        var azure = FakeAzure.NothingConfigured(FakeAzure.BasicTier);
        azure.IgnoredChanges.Add(FakeChange.AppSettingValues);

        var result = SetupScriptRun.RealRun(azure);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("app settings", result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Api, result.StandardError);
    }

    // The WebApp's Api__BaseUrl is checked by value too
    [Fact]
    public void RealRun_ApiBaseUrlValueDiffers_StopsNamingTheWebAppStep()
    {
        var azure = FakeAzure.FullyConfigured(FakeAzure.BasicTier);
        azure.Apps[WebApp].Settings.Clear();
        azure.IgnoredChanges.Add(FakeChange.AppSettingValues);

        var result = SetupScriptRun.RealRun(azure);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("app settings", result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"on {WebApp}", result.StandardError);
    }

    // A secret setting only has to exist: its value is never read back
    [Fact]
    public void RealRun_OnlySecretWritten_ChecksItsNameNotItsValue()
    {
        var azure = FakeAzure.FullyConfigured(FakeAzure.BasicTier);
        azure.Apps[Api].Settings.RemoveAll(setting => setting.Name == DryRunScenario.JwtSecretSetting);
        azure.IgnoredChanges.Add(FakeChange.AppSettingValues);

        var result = SetupScriptRun.RealRun(azure);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(result.FinalState.Apps[Api].Settings, setting => setting.Name == DryRunScenario.JwtSecretSetting);
        Assert.DoesNotContain(result.AzCalls, call => call.Contains($"[?name=='{DryRunScenario.JwtSecretSetting}'].value"));
    }

    // The generated JWT signing key reaches Azure but never the output
    [Fact]
    public void RealRun_JwtSecretWritten_NeverPrintsIt()
    {
        var result = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(FakeAzure.BasicTier));

        Assert.True(result.ExitCode == 0, result.Output);
        var secret = result.FinalState.Received(DryRunScenario.JwtSecretSetting);
        Assert.False(string.IsNullOrEmpty(secret), "The script sent no JWT signing key.");
        Assert.DoesNotContain(secret, result.Output);
    }

    // Not even the read-back error prints the secret
    [Theory]
    [InlineData(FakeChange.AppSettings)]
    [InlineData(FakeChange.AppSettingValues)]
    public void RealRun_AppSettingsNotApplied_ErrorDoesNotPrintSecret(string ignoredChange)
    {
        var azure = FakeAzure.NothingConfigured(FakeAzure.BasicTier);
        azure.IgnoredChanges.Add(ignoredChange);

        var result = SetupScriptRun.RealRun(azure);

        Assert.NotEqual(0, result.ExitCode);
        var secret = result.FinalState.Received(DryRunScenario.JwtSecretSetting);
        Assert.False(string.IsNullOrEmpty(secret), "The script sent no JWT signing key.");
        Assert.DoesNotContain(secret, result.Output);
    }
}

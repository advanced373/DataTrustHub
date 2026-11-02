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
    private const string SqlServer = "datatrusthub";

    // One change step of a real run
    // Ignored: the change the fake az accepts with exit 0 but does not apply
    // ChangeCall / DecidingRead: the change command and the read the step decides with
    // StepWords / Resource: what the read-back error must name: the step and the resource it changes
    private sealed record ChangeStep(string Ignored, string ChangeCall, string DecidingRead, string StepWords, string Resource);

    private static readonly ChangeStep[] Steps =
    [
        new(FakeChange.Identity, $"webapp identity assign {Rg} --name {Api} ",
            $"webapp show {Rg} --name {Api} --query identity.type", "managed identity", $"on {Api}"),
        new(FakeChange.AppSettings, $"webapp config appsettings set {Rg} --name {Api} ",
            $"webapp config appsettings list {Rg} --name {Api} --query [].name", "app settings", $"on {Api}"),
        new(FakeChange.Ftps, $"webapp config set {Rg} --name {Api} --ftps-state Disabled",
            $"webapp config show {Rg} --name {Api} --query ftpsState", "FTP", $"on {Api}"),
        new(FakeChange.BasicAuth, $"--parent sites/{Api} --name scm --set properties.allow=false",
            $"resource show {Rg} --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies --parent sites/{Api} --name scm --query properties.allow",
            "basic publishing credentials", $"on {Api}"),
        new(FakeChange.HealthCheck, $"webapp config set {Rg} --name {Api} --generic-configurations healthCheckPath=/health",
            $"webapp config show {Rg} --name {Api} --query healthCheckPath", "health check", $"of {Api}"),
        new(FakeChange.Firewall, $"sql server firewall-rule create {Rg} --server {SqlServer} --name {FakeAzure.FirewallRuleName}",
            $"sql server firewall-rule list {Rg} --server {SqlServer} --query [].name", "firewall", $"SQL server {SqlServer}"),
    ];

    // Each step's change command and the read that decides it
    public static TheoryData<string, string> ChangeAndDecidingRead
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var step in Steps)
                data.Add(step.ChangeCall, step.DecidingRead);
            return data;
        }
    }

    // Each step's ignored change, its change command, and what its read-back error must name
    public static TheoryData<string, string, string, string> IgnoredChangeAndError
    {
        get
        {
            var data = new TheoryData<string, string, string, string>();
            foreach (var step in Steps)
                data.Add(step.Ignored, step.ChangeCall, step.StepWords, step.Resource);
            return data;
        }
    }

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
    [MemberData(nameof(ChangeAndDecidingRead))]
    public void RealRun_AfterChange_ReadsTheValueBackWithTheDecidingRead(string changeCall, string decidingRead)
    {
        var result = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(FakeAzure.BasicTier));

        Assert.True(result.ExitCode == 0, result.Output);
        var calls = result.AzCalls.ToList();
        var change = calls.FindIndex(call => call.Contains(changeCall));
        Assert.True(change >= 0, $"No change call '{changeCall}' in:{Environment.NewLine}{string.Join(Environment.NewLine, calls)}");
        Assert.Contains(calls.Take(change), call => call.StartsWith(decidingRead));
        Assert.Contains(calls.Skip(change + 1), call => call.StartsWith(decidingRead));
    }

    // az exits 0 but changes nothing: the script stops with an error naming the step and its resource
    [Theory]
    [MemberData(nameof(IgnoredChangeAndError))]
    public void RealRun_ChangeNotApplied_StopsNamingTheStep(
        string ignoredChange, string changeCall, string stepWords, string resource)
    {
        var azure = FakeAzure.NothingConfigured(FakeAzure.BasicTier);
        azure.IgnoredChanges.Add(ignoredChange);

        var result = SetupScriptRun.RealRun(azure);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.AzCalls, call => call.Contains(changeCall));
        Assert.True(result.ErrorContains(stepWords, StringComparison.OrdinalIgnoreCase), $"The error does not name '{stepWords}':{Environment.NewLine}{result.StandardError}");
        Assert.True(result.ErrorContains(resource), $"The error does not name '{resource}':{Environment.NewLine}{result.StandardError}");
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
        Assert.True(result.ErrorContains("app settings", StringComparison.OrdinalIgnoreCase), result.StandardError);
        Assert.True(result.ErrorContains($"on {Api}"), result.StandardError);
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
        Assert.True(result.ErrorContains("app settings", StringComparison.OrdinalIgnoreCase), result.StandardError);
        Assert.True(result.ErrorContains($"on {WebApp}"), result.StandardError);
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
        Assert.False(result.OutputContains(secret), "The JWT signing key was printed, cut over two lines.");
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
        Assert.False(result.OutputContains(secret), "The JWT signing key was printed, cut over two lines.");
    }
}

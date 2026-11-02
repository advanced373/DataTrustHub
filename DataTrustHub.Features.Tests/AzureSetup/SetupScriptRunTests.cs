using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.AzureSetup.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.AzureSetup;

// #150 AC6: dry runs still change nothing, read nothing back and plan the same commands as before #150 for
// every step other than the health check. #150 AC7: a second run on a configured environment reports nothing
// to change. Runs against a fake az; the real Azure CLI is never on PATH.
public class SetupScriptRunTests
{
    private const string NothingToChange = "Nothing to change";

    // The commands a signed-out dry run planned before #150, without the two health check commands
    private static readonly string[] PlannedCommandsBefore150 =
    [
        "az webapp identity assign --resource-group DataTrustHub --name datatrusthubapi --identities [system]",
        "az webapp config appsettings set --resource-group DataTrustHub --name datatrusthubapi --settings \"@<temporary settings file>\"",
        "az webapp config appsettings set --resource-group DataTrustHub --name DataTrustHub --settings \"@<temporary settings file>\"",
        "az webapp config set --resource-group DataTrustHub --name datatrusthubapi --ftps-state Disabled",
        "az resource update --resource-group DataTrustHub --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies --parent sites/datatrusthubapi --name scm --set properties.allow=false",
        "az resource update --resource-group DataTrustHub --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies --parent sites/datatrusthubapi --name ftp --set properties.allow=false",
        "az webapp config set --resource-group DataTrustHub --name DataTrustHub --ftps-state Disabled",
        "az resource update --resource-group DataTrustHub --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies --parent sites/DataTrustHub --name scm --set properties.allow=false",
        "az resource update --resource-group DataTrustHub --namespace Microsoft.Web --resource-type basicPublishingCredentialsPolicies --parent sites/DataTrustHub --name ftp --set properties.allow=false",
        "az sql server firewall-rule create --resource-group DataTrustHub --server datatrusthub --name AllowAllWindowsAzureIps --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0",
    ];

    // The setting lines a signed-out dry run printed before #150
    private static readonly string[] PlannedSettingsBefore150 =
    [
        "ConnectionStrings__DefaultConnection=" + FakeAzure.ConnectionString,
        "Jwt__Secret=***",
        "Api__BaseUrl=https://<API default host name>",
    ];

    private static readonly Regex PlannedCommand = new(@"^\s*az .*$", RegexOptions.Multiline);

    // AC6: the plan of every other step is unchanged
    [Fact]
    public void DryRun_SignedOut_PlansTheSameCommandsAsBefore()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SignedOut, SetupScriptDryRun.WhatIfSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        var planned = PlannedCommand.Matches(result.Output).Select(match => match.Value.Trim()).ToList();
        Assert.Equal(PlannedCommandsBefore150, planned);
    }

    [Fact]
    public void DryRun_SignedOut_PlansTheSameSettingsAsBefore()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SignedOut, SetupScriptDryRun.WhatIfSwitch);

        var lines = result.Output.Split('\n').Select(line => line.Trim()).ToList();
        Assert.All(PlannedSettingsBefore150, setting => Assert.Contains(setting, lines));
    }

    // AC6: a dry run changes nothing, in the fake Azure as in the calls
    [Theory]
    [InlineData(FakeAzure.FreeTier)]
    [InlineData(FakeAzure.BasicTier)]
    public void DryRun_SignedIn_ChangesNothing(string tier)
    {
        var result = SetupScriptRun.DryRun(FakeAzure.NothingConfigured(tier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Empty(result.ChangeCalls);
        Assert.Empty(result.FinalState.ReceivedSettings);
        Assert.All(result.FinalState.Apps.Values, app => Assert.Null(app.IdentityType));
        Assert.Empty(result.FinalState.FirewallRules);
    }

    // AC6: no read back: a dry run with everything to change reads exactly what a dry run with nothing to
    // change reads (every read decides a step; none checks a change)
    [Theory]
    [InlineData(FakeAzure.FreeTier)]
    [InlineData(FakeAzure.BasicTier)]
    public void DryRun_SomethingToChange_ReadsNothingExtra(string tier)
    {
        var withChanges = SetupScriptRun.DryRun(FakeAzure.NothingConfigured(tier));
        var withoutChanges = SetupScriptRun.DryRun(FakeAzure.FullyConfigured(tier));

        Assert.True(withChanges.ExitCode == 0, withChanges.Output);
        Assert.True(withoutChanges.ExitCode == 0, withoutChanges.Output);
        Assert.Equal(withoutChanges.AzCalls, withChanges.AzCalls);
    }

    // AC7: a configured environment, on either tier, has nothing to change
    [Theory]
    [InlineData(FakeAzure.FreeTier)]
    [InlineData(FakeAzure.BasicTier)]
    public void RealRun_AlreadyConfigured_ReportsNothingToChange(string tier)
    {
        var result = SetupScriptRun.RealRun(FakeAzure.FullyConfigured(tier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Empty(result.ChangeCalls);
        Assert.Contains(NothingToChange, result.StandardOutput);
    }

    [Theory]
    [InlineData(FakeAzure.FreeTier)]
    [InlineData(FakeAzure.BasicTier)]
    public void DryRun_AlreadyConfigured_ReportsNothingToChange(string tier)
    {
        var result = SetupScriptRun.DryRun(FakeAzure.FullyConfigured(tier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(NothingToChange, result.StandardOutput);
    }

    // AC7: the second real run after a first one that set everything up changes nothing
    [Theory]
    [InlineData(FakeAzure.FreeTier)]
    [InlineData(FakeAzure.BasicTier)]
    public void RealRun_SecondRun_ReportsNothingToChange(string tier)
    {
        var first = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(tier));
        Assert.True(first.ExitCode == 0, first.Output);
        Assert.NotEmpty(first.ChangeCalls);

        var second = SetupScriptRun.RealRun(first.FinalState);

        Assert.True(second.ExitCode == 0, second.Output);
        Assert.Empty(second.ChangeCalls);
        Assert.Contains(NothingToChange, second.StandardOutput);
    }
}

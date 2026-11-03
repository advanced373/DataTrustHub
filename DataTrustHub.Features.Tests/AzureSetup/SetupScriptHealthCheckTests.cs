using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.AzureSetup.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.AzureSetup;

// #150 AC1-AC4, AC9 (ADR-0024): infra/setup.ps1 reads the pricing tier of each app's App Service plan from
// Azure and skips the health check on the Free tier, in dry runs and real runs; on a paid tier it sets the
// health check path as before. Every run is against a fake az; the real Azure CLI is never on PATH.
public class SetupScriptHealthCheckTests
{
    private const string Adr0024 = "ADR-0024";
    private const string HealthCheckChange = "--generic-configurations healthCheckPath=/health";
    private const string AppServicePlanStep = "App Service plan of ";
    private const string ResourceGroupArguments = "--resource-group DataTrustHub";

    // #177: the real Azure CLI holds the plan id of a web app under serverFarmId; appServicePlanId is empty
    private const string ServerFarmIdProperty = "serverFarmId";
    private const string AppServicePlanIdProperty = "appServicePlanId";
    private const string WebAppShow = "webapp show";
    private const string QueryArgument = "--query ";
    private const string CannotRead = "Cannot read";
    private const string WarningPrefix = "WARNING:";
    private const string RealRunWouldStopHere = "A real run would stop here.";
    private const string RealRunStopHeading = "A real run would stop with an error:";
    private const string ListItemPrefix = "- ";
    private const int PredictedStopExitCode = 1;

    private static readonly string[] Apps = [FakeAzure.ApiAppName, FakeAzure.WebAppName];

    // AC1 (real run): no health check change on the Free tier
    [Fact]
    public void RealRun_FreeTier_RunsNoHealthCheckChange()
    {
        var result = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(FakeAzure.FreeTier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(result.AzCalls, call => call.Contains(HealthCheckChange));
        Assert.All(result.FinalState.Apps.Values, app => Assert.Null(app.HealthCheckPath));
    }

    // AC1 (dry run, signed in): no health check change is planned on the Free tier
    [Fact]
    public void DryRun_SignedInFreeTier_PlansNoHealthCheckChange()
    {
        var result = SetupScriptRun.DryRun(FakeAzure.NothingConfigured(FakeAzure.FreeTier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(HealthCheckChange, result.Output);
    }

    // AC1: one informational line per app, saying the health check is skipped on the Free tier (ADR-0024)
    [Theory]
    [InlineData(FakeAzure.ApiAppName, false)]
    [InlineData(FakeAzure.WebAppName, false)]
    [InlineData(FakeAzure.ApiAppName, true)]
    [InlineData(FakeAzure.WebAppName, true)]
    public void Run_FreeTier_PrintsOneSkipLinePerApp(string appName, bool dryRun)
    {
        var azure = FakeAzure.NothingConfigured(FakeAzure.FreeTier);
        var result = dryRun ? SetupScriptRun.DryRun(azure) : SetupScriptRun.RealRun(azure);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Single(SkipLines(result.StandardOutput, appName));
    }

    // AC4: signed out, the dry run assumes the production tier, Free
    [Fact]
    public void DryRun_SignedOut_PlansNoHealthCheckChange()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SignedOut, SetupScriptDryRun.WhatIfSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(HealthCheckChange, result.Output);
    }

    [Theory]
    [InlineData(FakeAzure.ApiAppName)]
    [InlineData(FakeAzure.WebAppName)]
    public void DryRun_SignedOut_PrintsOneSkipLinePerApp(string appName)
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SignedOut, SetupScriptDryRun.WhatIfSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Single(SkipLines(result.Output, appName));
    }

    // AC2 (real run): on a paid tier the health check path is read and set to /health on both apps
    [Theory]
    [InlineData(FakeAzure.ApiAppName)]
    [InlineData(FakeAzure.WebAppName)]
    public void RealRun_BasicTierWithoutHealthCheck_SetsHealthCheckPath(string appName)
    {
        var result = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(FakeAzure.BasicTier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(result.AzCalls, call => call.Contains(HealthCheckRead(appName)));
        Assert.Contains(result.AzCalls, call => IsHealthCheckChange(call, appName));
        Assert.Equal(FakeAzure.HealthCheckPath, result.FinalState.Apps[appName].HealthCheckPath);
    }

    // AC2: a different path is replaced by /health
    [Fact]
    public void RealRun_BasicTierWithOtherHealthCheckPath_SetsHealthCheckPath()
    {
        var azure = FakeAzure.FullyConfigured(FakeAzure.BasicTier);
        azure.Apps[FakeAzure.ApiAppName].HealthCheckPath = "/other";

        var result = SetupScriptRun.RealRun(azure);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(result.AzCalls, call => IsHealthCheckChange(call, FakeAzure.ApiAppName));
        Assert.DoesNotContain(result.AzCalls, call => IsHealthCheckChange(call, FakeAzure.WebAppName));
        Assert.Equal(FakeAzure.HealthCheckPath, result.FinalState.Apps[FakeAzure.ApiAppName].HealthCheckPath);
    }

    // AC2: only if it differs
    [Fact]
    public void RealRun_BasicTierWithHealthCheckSet_RunsNoHealthCheckChange()
    {
        var result = SetupScriptRun.RealRun(FakeAzure.FullyConfigured(FakeAzure.BasicTier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(result.AzCalls, call => call.Contains(HealthCheckChange));
    }

    // AC2 (dry run): on a paid tier the health check change is planned for both apps, and nothing is skipped
    [Theory]
    [InlineData(FakeAzure.ApiAppName)]
    [InlineData(FakeAzure.WebAppName)]
    public void DryRun_BasicTier_PlansHealthCheckChange(string appName)
    {
        var result = SetupScriptRun.DryRun(FakeAzure.NothingConfigured(FakeAzure.BasicTier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(result.StandardOutput.Split('\n'), line => IsHealthCheckChange(line, appName));
        Assert.Empty(SkipLines(result.StandardOutput, appName));
    }

    // AC3: the tier comes from Azure, from the plan the web app belongs to
    [Theory]
    [InlineData(FakeAzure.ApiAppName)]
    [InlineData(FakeAzure.WebAppName)]
    public void RealRun_ReadsTierOfTheAppsOwnPlan(string appName)
    {
        var result = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(FakeAzure.FreeTier));

        var planRead = result.AzCalls.ToList().FindIndex(call =>
            Regex.IsMatch(call, FakeRead.PlanId) && call.Contains($"--name {appName} "));
        Assert.True(planRead >= 0, string.Join(Environment.NewLine, result.AzCalls));
        Assert.Contains(result.AzCalls.Skip(planRead), call =>
            Regex.IsMatch(call, FakeRead.PlanTier) && call.Contains($"--ids {FakeAzure.ProductionPlanId} "));
    }

    // AC3: two apps on plans of different tiers: only the app on the paid plan gets the health check
    [Fact]
    public void RealRun_AppsOnDifferentPlans_SetsHealthCheckOnlyOnPaidPlan()
    {
        var azure = FakeAzure.NothingConfigured(FakeAzure.FreeTier);
        azure.Plans.Add(new FakePlan(FakeAzure.OtherPlanId, FakeAzure.BasicTier));
        azure.Apps[FakeAzure.ApiAppName].PlanId = FakeAzure.OtherPlanId;

        var result = SetupScriptRun.RealRun(azure);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(result.AzCalls, call => IsHealthCheckChange(call, FakeAzure.ApiAppName));
        Assert.DoesNotContain(result.AzCalls, call => IsHealthCheckChange(call, FakeAzure.WebAppName));
        Assert.Single(SkipLines(result.StandardOutput, FakeAzure.WebAppName));
    }

    // AC3: a failed read of the plan or its tier stops the script with the step named, in both kinds of run
    [Theory]
    [InlineData(FakeRead.PlanId, false)]
    [InlineData(FakeRead.PlanTier, false)]
    [InlineData(FakeRead.PlanId, true)]
    [InlineData(FakeRead.PlanTier, true)]
    public void Run_PlanReadFails_StopsNamingTheStep(string failingRead, bool dryRun)
    {
        var azure = FakeAzure.NothingConfigured(FakeAzure.BasicTier);
        azure.FailingReads.Add(failingRead);

        var result = dryRun ? SetupScriptRun.DryRun(azure) : SetupScriptRun.RealRun(azure);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(AppServicePlanStep + FakeAzure.ApiAppName, result.StandardError);
        Assert.DoesNotContain(HealthCheckChange, result.Output);
        Assert.DoesNotContain(result.AzCalls, call => call.Contains(HealthCheckChange));
    }

    // #177 AC3 (replaces #150 AC9 for a signed-in dry run): an empty plan id or tier is warned about, naming
    // the step for each app, instead of being taken for the Free tier
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DryRun_SignedInEmptyPlanIdOrTier_WarnsNamingTheStep(bool emptyPlanId)
    {
        var result = SetupScriptRun.DryRun(WithEmptyPlan(FakeAzure.NothingConfigured(FakeAzure.BasicTier), emptyPlanId));

        Assert.All(Apps, appName => Assert.Contains(result.Output.Split('\n'), line =>
            line.Contains(WarningPrefix) && line.Contains(CannotRead) && line.Contains(AppServicePlanStep + appName)
            && line.Contains(RealRunWouldStopHere)));
    }

    // #177 AC3: the dry run does not claim the health check would be skipped for the Free tier
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DryRun_SignedInEmptyPlanIdOrTier_PrintsNoFreeTierSkipLine(bool emptyPlanId)
    {
        var result = SetupScriptRun.DryRun(WithEmptyPlan(FakeAzure.NothingConfigured(FakeAzure.BasicTier), emptyPlanId));

        Assert.All(Apps, appName => Assert.Empty(SkipLines(result.Output, appName)));
    }

    // #177 AC3: with the tier unknown, no health check change is planned, and nothing changes Azure
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DryRun_SignedInEmptyPlanIdOrTier_PlansNoHealthCheckChange(bool emptyPlanId)
    {
        var result = SetupScriptRun.DryRun(WithEmptyPlan(FakeAzure.NothingConfigured(FakeAzure.BasicTier), emptyPlanId));

        Assert.DoesNotContain(HealthCheckChange, result.Output);
        Assert.Empty(result.ChangeCalls);
    }

    // #177 AC3: the summary predicts the failing real run, listing the step of each app
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DryRun_SignedInEmptyPlanIdOrTier_ListsTheStepAsRealRunStop(bool emptyPlanId)
    {
        var result = SetupScriptRun.DryRun(WithEmptyPlan(FakeAzure.NothingConfigured(FakeAzure.BasicTier), emptyPlanId));

        var stops = PredictedStops(result.StandardOutput);
        Assert.All(Apps, appName => Assert.Contains(stops, stop =>
            stop.Contains(CannotRead) && stop.Contains(AppServicePlanStep + appName)));
    }

    // #177 AC3: the exit code of the dry run says a real run would fail
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DryRun_SignedInEmptyPlanIdOrTier_ExitsWithPredictedStopCode(bool emptyPlanId)
    {
        var result = SetupScriptRun.DryRun(WithEmptyPlan(FakeAzure.NothingConfigured(FakeAzure.BasicTier), emptyPlanId));

        Assert.True(result.ExitCode == PredictedStopExitCode, result.Output);
    }

    // #177 AC3: a signed-in dry run that reads the tier predicts no stop (the summary section stays out)
    [Theory]
    [InlineData(FakeAzure.FreeTier)]
    [InlineData(FakeAzure.BasicTier)]
    public void DryRun_SignedInTierRead_ListsNoRealRunStop(string tier)
    {
        var result = SetupScriptRun.DryRun(FakeAzure.NothingConfigured(tier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(RealRunStopHeading, result.Output);
        Assert.DoesNotContain(CannotRead, result.Output);
    }

    // #177 AC3: signed out, the dry run behaves as before: it assumes Free, predicts no stop and exits 0
    [Fact]
    public void DryRun_SignedOut_AssumesFreeTierWithoutPredictedStop()
    {
        var result = SetupScriptDryRun.Run(DryRunScenario.SignedOut, SetupScriptDryRun.WhatIfSwitch);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.DoesNotContain(RealRunStopHeading, result.Output);
        Assert.DoesNotContain(CannotRead, result.Output);
        Assert.All(Apps, appName => Assert.Single(SkipLines(result.Output, appName)));
    }

    // AC9: a real run that reads an empty plan id or tier stops with an error naming the step
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RealRun_EmptyPlanIdOrTier_StopsNamingTheStep(bool emptyPlanId)
    {
        var azure = WithEmptyPlan(FakeAzure.NothingConfigured(FakeAzure.BasicTier), emptyPlanId);

        var result = SetupScriptRun.RealRun(azure);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(AppServicePlanStep + FakeAzure.ApiAppName, result.StandardError);
        Assert.DoesNotContain(result.AzCalls, call => call.Contains(HealthCheckChange));
        Assert.Empty(SkipLines(result.StandardOutput, FakeAzure.ApiAppName));
    }

    // #177 AC1: against an az that holds the plan id only under serverFarmId (like the real CLI), a real run on
    // a paid tier reads the tier, sets the health check and goes on to the firewall rule
    [Fact]
    public void RealRun_PlanIdUnderServerFarmIdOnly_ContinuesPastHealthCheckStep()
    {
        var result = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(FakeAzure.BasicTier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.All(Apps, appName => Assert.Equal(FakeAzure.HealthCheckPath, result.FinalState.Apps[appName].HealthCheckPath));
        Assert.Contains(FakeAzure.FirewallRuleName, result.FinalState.FirewallRules);
        Assert.DoesNotContain(CannotRead, result.Output);
    }

    // #177 AC1: on the Free tier the real run reads the tier, skips the health check and goes on
    [Fact]
    public void RealRun_PlanIdUnderServerFarmIdOnlyFreeTier_SkipsHealthCheckAndContinues()
    {
        var result = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(FakeAzure.FreeTier));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.All(Apps, appName => Assert.Single(SkipLines(result.StandardOutput, appName)));
        Assert.Contains(FakeAzure.FirewallRuleName, result.FinalState.FirewallRules);
    }

    // #177 AC1: the plan id of each app is read from serverFarmId, never from appServicePlanId
    [Theory]
    [InlineData(FakeAzure.ApiAppName)]
    [InlineData(FakeAzure.WebAppName)]
    public void RealRun_ReadsPlanIdFromServerFarmId(string appName)
    {
        var result = SetupScriptRun.RealRun(FakeAzure.NothingConfigured(FakeAzure.BasicTier));

        Assert.Contains(result.AzCalls, call => call.StartsWith(WebAppShow, StringComparison.Ordinal)
            && call.Contains($"--name {appName} ") && call.Contains(QueryArgument + ServerFarmIdProperty));
        Assert.DoesNotContain(result.AzCalls, call => call.Contains(AppServicePlanIdProperty));
    }

    // #177 AC2 (regression): a script that still queried appServicePlanId gets an empty answer from the fake,
    // as from the real CLI, so its real run stops at the plan step and sets no health check
    [Fact]
    public void RealRun_ScriptQueryingAppServicePlanId_StopsAtThePlanStep()
    {
        var result = SetupScriptRun.RealRunOfVariant(FakeAzure.NothingConfigured(FakeAzure.BasicTier),
            QuotedProperty(ServerFarmIdProperty), QuotedProperty(AppServicePlanIdProperty));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.AzCalls, call => call.Contains(QueryArgument + AppServicePlanIdProperty));
        Assert.True(result.ErrorContains($"{CannotRead} the {AppServicePlanStep}{FakeAzure.ApiAppName}"), result.Output);
        Assert.All(result.FinalState.Apps.Values, app => Assert.Null(app.HealthCheckPath));
    }

    // Lines listed under "A real run would stop with an error:" in the summary
    private static List<string> PredictedStops(string output) =>
        output.Split('\n')
            .SkipWhile(line => !line.Contains(RealRunStopHeading))
            .Skip(1)
            .TakeWhile(line => line.TrimStart().StartsWith(ListItemPrefix, StringComparison.Ordinal))
            .ToList();

    private static string QuotedProperty(string property) => $"'{property}'";

    private static FakeAzure WithEmptyPlan(FakeAzure azure, bool emptyPlanId)
    {
        if (emptyPlanId)
        {
            foreach (var app in azure.Apps.Values)
                app.PlanId = null;
        }
        else
        {
            azure.Plans = [new FakePlan(FakeAzure.ProductionPlanId, null)];
        }
        return azure;
    }

    // Lines saying the health check of the app is skipped on the Free tier, with a reference to ADR-0024
    private static List<string> SkipLines(string output, string appName) =>
        output.Split('\n')
            .Where(line => line.Contains("health check", StringComparison.OrdinalIgnoreCase))
            .Where(line => line.Contains("skip", StringComparison.OrdinalIgnoreCase))
            .Where(line => line.Contains(FakeAzure.FreeTier, StringComparison.Ordinal) && line.Contains(Adr0024, StringComparison.Ordinal))
            .Where(line => Regex.IsMatch(line, $@"(?<![\w-]){Regex.Escape(appName)}(?![\w-])"))
            .ToList();

    private static bool IsHealthCheckChange(string line, string appName) =>
        line.Contains(HealthCheckChange) && line.Contains($"{ResourceGroupArguments} --name {appName} ");

    private static string HealthCheckRead(string appName) =>
        $"webapp config show {ResourceGroupArguments} --name {appName} --query healthCheckPath";
}

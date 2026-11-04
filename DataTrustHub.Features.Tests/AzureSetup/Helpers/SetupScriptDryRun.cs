namespace DataTrustHub.Features.Tests.AzureSetup.Helpers;

/// <summary>The outcome of one dry run of the setup script against the fake az.</summary>
/// <param name="ExitCode">The exit code of the PowerShell process.</param>
/// <param name="Output">Standard output and standard error, in that order.</param>
/// <param name="AzCalls">Every az command line the script ran, as the fake recorded it.</param>
internal sealed record DryRunResult(int ExitCode, string Output, IReadOnlyList<string> AzCalls);

/// <summary>
/// Runs infra/setup.ps1 -WhatIf with a fake az (see <see cref="SetupScriptProcess"/>). The fake answers the
/// read-only commands from a <see cref="DryRunScenario"/>, records every call and fails any command it does
/// not know, so a change command would show up. The App Service plan of every app is on the scenario's tier
/// (the production tier, Free, unless the scenario says otherwise).
/// </summary>
internal static class SetupScriptDryRun
{
    internal const string WhatIfSwitch = "-WhatIf";
    internal const string RotateJwtSecretSwitch = "-RotateJwtSecret";
    internal const string SeedAdminSwitch = "-SeedAdmin";
    internal const string FakeApiHostName = "fake-api.example.test";
    internal const string FakeSubscriptionName = "Fake Subscription";
    internal const string FakePlanId = "/subscriptions/fake/resourceGroups/DataTrustHub/providers/Microsoft.Web/serverfarms/DataTrustHubPlan";

    private const string CallLogFile = "az-calls.log";
    private const string TrueValue = "true";
    private const string SettingNameSeparator = ",";

    // The fake Azure CLI. Unknown commands exit 99, which the script treats as a failure.
    private const string FakeAz = """
        $call = $args -join ' '
        Add-Content -LiteralPath $env:FAKE_AZ_LOG -Value $call
        if ($env:FAKE_AZ_SIGNED_IN -ne 'true') { [Console]::Error.WriteLine('Please run az login.'); exit 1 }
        if ($call -match '^account show') { $env:FAKE_AZ_SUBSCRIPTION; exit 0 }
        if ($call -match '^webapp config appsettings list') {
            if ($env:FAKE_AZ_FAIL_SETTINGS -eq 'true') { [Console]::Error.WriteLine('Forbidden'); exit 1 }
            if ($call -match '\[\]\.name') { $env:FAKE_AZ_SETTING_NAMES -split ',' | Where-Object { $_ } }
            exit 0
        }
        if ($call -match '^webapp show .*defaultHostName') { $env:FAKE_AZ_HOST_NAME; exit 0 }
        if ($call -match '^webapp show .*identity\.type') { exit 0 }
        if ($call -match '^webapp show .*serverFarmId') { $env:FAKE_AZ_PLAN_ID; exit 0 }
        if ($call -match '^webapp show .*appServicePlanId') { exit 0 }
        if ($call -match '^appservice plan show .*sku\.tier') { $env:FAKE_AZ_PLAN_TIER; exit 0 }
        if ($call -match '^webapp config show') { exit 0 }
        if ($call -match '^resource show') { exit 0 }
        if ($call -match '^sql server firewall-rule list') { exit 0 }
        [Console]::Error.WriteLine("Fake az: unexpected command: $call")
        exit 99
        """;

    internal static DryRunResult Run(DryRunScenario scenario, params string[] switches)
    {
        var workFolder = Directory.CreateTempSubdirectory("dth-setup-dryrun-");
        try
        {
            return RunIn(workFolder.FullName, scenario, switches);
        }
        finally
        {
            workFolder.Delete(recursive: true);
        }
    }

    private static DryRunResult RunIn(string workFolder, DryRunScenario scenario, string[] switches)
    {
        var fakeAzFolder = SetupScriptProcess.WriteFakeAz(workFolder, FakeAz);
        var callLog = Path.Combine(workFolder, CallLogFile);
        File.WriteAllText(callLog, string.Empty);

        var environment = new Dictionary<string, string>
        {
            ["FAKE_AZ_LOG"] = callLog,
            ["FAKE_AZ_SIGNED_IN"] = scenario.SignedIn ? TrueValue : string.Empty,
            ["FAKE_AZ_FAIL_SETTINGS"] = scenario.AppSettingsReadFails ? TrueValue : string.Empty,
            ["FAKE_AZ_SETTING_NAMES"] = string.Join(SettingNameSeparator, scenario.ApiSettingNames),
            ["FAKE_AZ_HOST_NAME"] = FakeApiHostName,
            ["FAKE_AZ_SUBSCRIPTION"] = FakeSubscriptionName,
            ["FAKE_AZ_PLAN_ID"] = FakePlanId,
            ["FAKE_AZ_PLAN_TIER"] = scenario.PlanTier,
        };

        var result = SetupScriptProcess.Run(workFolder, fakeAzFolder, environment, switches);
        var calls = File.ReadAllLines(callLog).Where(line => line.Length > 0).ToList();
        return new DryRunResult(result.ExitCode,
            result.StandardOutput + Environment.NewLine + result.StandardError, calls);
    }
}

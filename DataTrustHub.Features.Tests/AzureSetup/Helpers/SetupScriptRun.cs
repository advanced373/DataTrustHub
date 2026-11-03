using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.Build.Helpers;

namespace DataTrustHub.Features.Tests.AzureSetup.Helpers;

/// <summary>The outcome of one run of the setup script against the stateful fake az.</summary>
/// <param name="ExitCode">The exit code of the PowerShell process.</param>
/// <param name="StandardOutput">What the script printed.</param>
/// <param name="StandardError">The errors the script wrote.</param>
/// <param name="AzCalls">Every az command line the script ran, in order, as the fake recorded it.</param>
/// <param name="FinalState">The fake Azure state after the run.</param>
internal sealed record SetupRunResult(int ExitCode, string StandardOutput, string StandardError,
    IReadOnlyList<string> AzCalls, FakeAzure FinalState)
{
    // An az verb that changes Azure
    private static readonly Regex ChangeVerb = new(@"\b(create|set|assign|update|delete|remove)\b");

    internal string Output => StandardOutput + Environment.NewLine + StandardError;

    /// <summary>Tells whether the script's error holds the text, however PowerShell laid the error out.</summary>
    internal bool ErrorContains(string text, StringComparison comparison = StringComparison.Ordinal) =>
        RenderedError.Contains(StandardError, text, comparison);

    /// <summary>Tells whether the text was printed anywhere, even cut over two lines of a rendered error.</summary>
    internal bool OutputContains(string text) => RenderedError.Contains(Output, text);

    /// <summary>The az calls that change Azure (none of them in a dry run).</summary>
    internal IReadOnlyList<string> ChangeCalls => AzCalls.Where(call => ChangeVerb.IsMatch(call)).ToList();
}

/// <summary>
/// Runs infra/setup.ps1 (a real run with -Force, or a dry run with -WhatIf) against a stateful fake az
/// (see <see cref="SetupScriptProcess"/>: the real Azure CLI is never on PATH). The fake reads and changes a
/// <see cref="FakeAzure"/> state held in a JSON file, records every call, can be told to fail a read or to
/// ignore a change (exit 0, nothing changed), and fails any command it does not know.
/// </summary>
internal static class SetupScriptRun
{
    internal const string ForceSwitch = "-Force";
    internal const string ApiBaseUrlSetting = "Api__BaseUrl";

    private const string CallLogFile = "az-calls.log";
    private const string StateFile = "azure-state.json";
    private const string VariantScriptFile = "setup-variant.ps1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // The stateful fake Azure CLI. It runs inside the script's PowerShell session, so it turns strict mode off.
    private const string FakeAz = """
        Set-StrictMode -Off
        $fakeArgs = @($args)
        $call = $fakeArgs -join ' '
        Add-Content -LiteralPath $env:FAKE_AZ_LOG -Value $call
        $state = [IO.File]::ReadAllText($env:FAKE_AZ_STATE) | ConvertFrom-Json

        function Get-Flag([string] $Flag) {
            $index = [array]::IndexOf($fakeArgs, $Flag)
            if ($index -ge 0) { return $fakeArgs[$index + 1] }
            return $null
        }
        function Stop-Fake([string] $Message, [int] $Code = 1) { [Console]::Error.WriteLine($Message); exit $Code }
        function Save-State { [IO.File]::WriteAllText($env:FAKE_AZ_STATE, (ConvertTo-Json -InputObject $state -Depth 10)) }
        function Test-Ignored([string] $Kind) { return @($state.ignoredChanges) -contains $Kind }
        function Get-App([string] $Name) {
            $app = $state.apps.$Name
            if (-not $app) { Stop-Fake "Fake az: no app $Name" 3 }
            return $app
        }
        function Write-Value($Value) { if ($Value) { [string] $Value } }

        if (-not $state.signedIn) { Stop-Fake 'Please run az login.' }
        foreach ($pattern in @($state.failingReads)) { if ($call -match $pattern) { Stop-Fake 'Forbidden' } }

        if ($call -match '^account show') { Write-Value $state.subscription; exit 0 }
        if ($call -match '^webapp show') {
            $app = Get-App (Get-Flag '--name')
            $query = Get-Flag '--query'
            # Like the real Azure CLI (#177): the plan id is under serverFarmId, appServicePlanId answers empty
            if ($query -eq 'appServicePlanId') { exit 0 }
            $property = @{ 'identity.type' = 'identityType'; 'defaultHostName' = 'defaultHostName'; 'serverFarmId' = 'planId' }[$query]
            if (-not $property) { Stop-Fake "Fake az: unexpected command: $call" 99 }
            Write-Value $app.$property; exit 0
        }
        if ($call -match '^appservice plan show') {
            $planId = Get-Flag '--ids'
            $plan = @($state.plans | Where-Object { $_.id -eq $planId })
            if ($plan.Count -eq 0) { Stop-Fake "Fake az: no plan $planId" 3 }
            if ((Get-Flag '--query') -ne 'sku.tier') { Stop-Fake "Fake az: unexpected command: $call" 99 }
            Write-Value $plan[0].tier; exit 0
        }
        if ($call -match '^webapp config appsettings list') {
            $app = Get-App (Get-Flag '--name')
            $query = Get-Flag '--query'
            if ($query -eq '[].name') { foreach ($setting in @($app.settings)) { $setting.name }; exit 0 }
            if ($query -match "^\[\?name=='(?<name>[^']+)'\]\.value$") {
                $wanted = $Matches['name']
                foreach ($setting in @($app.settings)) { if ($setting.name -eq $wanted) { Write-Value $setting.value } }
                exit 0
            }
            Stop-Fake "Fake az: unexpected command: $call" 99
        }
        if ($call -match '^webapp config appsettings set') {
            $app = Get-App (Get-Flag '--name')
            $entries = [IO.File]::ReadAllText((Get-Flag '--settings').Substring(1)) | ConvertFrom-Json
            foreach ($entry in $entries) {
                $state.receivedSettings = @(@($state.receivedSettings) + [pscustomobject] @{ name = $entry.name; value = $entry.value })
                if (Test-Ignored 'appsettings') { continue }
                $value = $entry.value
                if (Test-Ignored 'appsetting-values') { $value = 'not-the-target-value' }
                $kept = @(@($app.settings) | Where-Object { $_.name -ne $entry.name })
                $app.settings = @($kept + [pscustomobject] @{ name = $entry.name; value = $value })
            }
            Save-State; exit 0
        }
        if ($call -match '^webapp config show') {
            $app = Get-App (Get-Flag '--name')
            $property = Get-Flag '--query'
            if (@('ftpsState', 'healthCheckPath') -notcontains $property) { Stop-Fake "Fake az: unexpected command: $call" 99 }
            Write-Value $app.$property; exit 0
        }
        if ($call -match '^webapp config set') {
            $app = Get-App (Get-Flag '--name')
            $ftpsState = Get-Flag '--ftps-state'
            $generic = Get-Flag '--generic-configurations'
            if ($ftpsState) {
                if (-not (Test-Ignored 'ftps')) { $app.ftpsState = $ftpsState; Save-State }
                exit 0
            }
            if ($generic -match '^healthCheckPath=(?<path>.+)$') {
                $path = $Matches['path']
                if (-not (Test-Ignored 'healthcheck')) { $app.healthCheckPath = $path; Save-State }
                exit 0
            }
            Stop-Fake "Fake az: unexpected command: $call" 99
        }
        if ($call -match '^webapp identity assign') {
            $app = Get-App (Get-Flag '--name')
            if (-not (Test-Ignored 'identity')) { $app.identityType = 'SystemAssigned'; Save-State }
            exit 0
        }
        if ($call -match '^resource (show|update)') {
            $app = Get-App ((Get-Flag '--parent') -replace '^sites/', '')
            $policy = Get-Flag '--name'
            if ($call -match '^resource show') { Write-Value $app.basicAuth.$policy; exit 0 }
            if (-not (Test-Ignored 'basicauth')) {
                $app.basicAuth.$policy = (Get-Flag '--set') -replace '^properties\.allow=', ''
                Save-State
            }
            exit 0
        }
        if ($call -match '^sql server firewall-rule list') { foreach ($rule in @($state.firewallRules)) { $rule }; exit 0 }
        if ($call -match '^sql server firewall-rule create') {
            if (-not (Test-Ignored 'firewall')) { $state.firewallRules = @(@($state.firewallRules) + (Get-Flag '--name')); Save-State }
            exit 0
        }
        Stop-Fake "Fake az: unexpected command: $call" 99
        """;

    /// <summary>A real run: -Force (no confirmation prompt) plus the given switches.</summary>
    internal static SetupRunResult RealRun(FakeAzure azure, params string[] switches) =>
        Run(azure, [ForceSwitch, .. switches]);

    /// <summary>A dry run: -WhatIf plus the given switches.</summary>
    internal static SetupRunResult DryRun(FakeAzure azure, params string[] switches) =>
        Run(azure, [SetupScriptDryRun.WhatIfSwitch, .. switches]);

    /// <summary>
    /// A real run (-Force) of a variant of infra/setup.ps1: its text with <paramref name="oldText"/> replaced by
    /// <paramref name="newText"/>, run from a temporary copy. Lets a test show the fake catches a regression.
    /// </summary>
    internal static SetupRunResult RealRunOfVariant(FakeAzure azure, string oldText, string newText)
    {
        var original = File.ReadAllText(Path.Combine(SolutionFiles.RepositoryRoot, SetupScriptParser.SetupScriptFile));
        if (!original.Contains(oldText, StringComparison.Ordinal))
            throw new InvalidOperationException($"infra/setup.ps1 does not contain: {oldText}");
        return Run(azure, [ForceSwitch], original.Replace(oldText, newText, StringComparison.Ordinal));
    }

    private static SetupRunResult Run(FakeAzure azure, string[] switches, string? scriptText = null)
    {
        var workFolder = Directory.CreateTempSubdirectory("dth-setup-run-");
        try
        {
            return RunIn(workFolder.FullName, azure, switches, scriptText);
        }
        finally
        {
            workFolder.Delete(recursive: true);
        }
    }

    private static SetupRunResult RunIn(string workFolder, FakeAzure azure, string[] switches, string? scriptText)
    {
        var scriptPath = WriteVariant(workFolder, scriptText);
        var fakeAzFolder = SetupScriptProcess.WriteFakeAz(workFolder, FakeAz);
        var callLog = Path.Combine(workFolder, CallLogFile);
        var stateFile = Path.Combine(workFolder, StateFile);
        File.WriteAllText(callLog, string.Empty);
        File.WriteAllText(stateFile, JsonSerializer.Serialize(azure, JsonOptions));

        var environment = new Dictionary<string, string> { ["FAKE_AZ_LOG"] = callLog, ["FAKE_AZ_STATE"] = stateFile };
        var result = SetupScriptProcess.Run(workFolder, fakeAzFolder, environment, switches, scriptPath);

        var calls = File.ReadAllLines(callLog).Where(line => line.Length > 0).ToList();
        var finalState = JsonSerializer.Deserialize<FakeAzure>(File.ReadAllText(stateFile), JsonOptions)
            ?? throw new InvalidOperationException("The fake Azure state is empty.");
        return new SetupRunResult(result.ExitCode, result.StandardOutput, result.StandardError, calls, finalState);
    }

    // Writes the variant script (with a byte order mark, so Windows PowerShell 5.1 reads it as UTF-8), or
    // returns null to run infra/setup.ps1 itself
    private static string? WriteVariant(string workFolder, string? scriptText)
    {
        if (scriptText is null)
            return null;
        var scriptPath = Path.Combine(workFolder, VariantScriptFile);
        File.WriteAllText(scriptPath, scriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return scriptPath;
    }
}

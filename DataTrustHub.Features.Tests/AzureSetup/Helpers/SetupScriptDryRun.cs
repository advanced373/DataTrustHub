using System.Diagnostics;
using System.Runtime.InteropServices;
using DataTrustHub.Features.Tests.Build.Helpers;

namespace DataTrustHub.Features.Tests.AzureSetup.Helpers;

/// <summary>The outcome of one dry run of the setup script against the fake az.</summary>
/// <param name="ExitCode">The exit code of the PowerShell process.</param>
/// <param name="Output">Standard output and standard error, in that order.</param>
/// <param name="AzCalls">Every az command line the script ran, as the fake recorded it.</param>
internal sealed record DryRunResult(int ExitCode, string Output, IReadOnlyList<string> AzCalls);

/// <summary>
/// Runs infra/setup.ps1 -WhatIf in a separate PowerShell process (Windows PowerShell 5.1 on Windows, pwsh
/// elsewhere) with a fake az. The real Azure CLI can never be reached: PATH holds only a temporary folder
/// with the fake (az.ps1, which PowerShell resolves for "az" on every platform) and AZURE_CONFIG_DIR points
/// at an empty temporary folder. The fake answers the read-only commands from a <see cref="DryRunScenario"/>,
/// records every call and fails any command it does not know, so a change command would show up.
/// </summary>
internal static class SetupScriptDryRun
{
    internal const string WhatIfSwitch = "-WhatIf";
    internal const string RotateJwtSecretSwitch = "-RotateJwtSecret";
    internal const string SeedAdminSwitch = "-SeedAdmin";
    internal const string FakeApiHostName = "fake-api.example.test";
    internal const string FakeSubscriptionName = "Fake Subscription";

    private const string WindowsPowerShell = "powershell.exe";
    private const string PowerShell7 = "pwsh";
    private const string FakeAzFile = "az.ps1";
    private const string CallLogFile = "az-calls.log";
    private const string TrueValue = "true";
    private const string SettingNameSeparator = ",";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

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
        var fakeAzFolder = Directory.CreateDirectory(Path.Combine(workFolder, "bin")).FullName;
        var azureConfigFolder = Directory.CreateDirectory(Path.Combine(workFolder, "azure-config")).FullName;
        var callLog = Path.Combine(workFolder, CallLogFile);
        File.WriteAllText(Path.Combine(fakeAzFolder, FakeAzFile), FakeAz);
        File.WriteAllText(callLog, string.Empty);

        var startInfo = StartInfo(switches);
        startInfo.Environment["PATH"] = fakeAzFolder;
        startInfo.Environment["AZURE_CONFIG_DIR"] = azureConfigFolder;
        startInfo.Environment["FAKE_AZ_LOG"] = callLog;
        startInfo.Environment["FAKE_AZ_SIGNED_IN"] = scenario.SignedIn ? TrueValue : string.Empty;
        startInfo.Environment["FAKE_AZ_FAIL_SETTINGS"] = scenario.AppSettingsReadFails ? TrueValue : string.Empty;
        startInfo.Environment["FAKE_AZ_SETTING_NAMES"] = string.Join(SettingNameSeparator, scenario.ApiSettingNames);
        startInfo.Environment["FAKE_AZ_HOST_NAME"] = FakeApiHostName;
        startInfo.Environment["FAKE_AZ_SUBSCRIPTION"] = FakeSubscriptionName;

        var (exitCode, output) = Execute(startInfo);
        var calls = File.ReadAllLines(callLog).Where(line => line.Length > 0).ToList();
        return new DryRunResult(exitCode, output, calls);
    }

    private static ProcessStartInfo StartInfo(string[] switches)
    {
        var startInfo = new ProcessStartInfo(ShellExecutable())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Bypass applies to this process only; it lets Windows PowerShell run the script and the fake
        string[] arguments = ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(SolutionFiles.RepositoryRoot, SetupScriptParser.SetupScriptFile)];
        foreach (var argument in arguments.Concat(switches))
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static (int ExitCode, string Output) Execute(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("PowerShell did not start.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The setup script dry run did not finish within {Timeout}.");
        }
        process.WaitForExit();
        return (process.ExitCode, output.Result + Environment.NewLine + error.Result);
    }

    private static string ShellExecutable() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsPowerShell : PowerShell7;
}

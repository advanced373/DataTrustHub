using System.Diagnostics;
using System.Runtime.InteropServices;
using DataTrustHub.Features.Tests.Build.Helpers;

namespace DataTrustHub.Features.Tests.AzureSetup.Helpers;

/// <summary>The standard output, standard error and exit code of one run of the setup script.</summary>
internal sealed record ScriptProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Runs infra/setup.ps1 in a separate PowerShell process (Windows PowerShell 5.1 on Windows, pwsh elsewhere)
/// with a fake az. The real Azure CLI can never be reached: PATH holds only the given folder with the fake
/// (az.ps1, which PowerShell resolves for "az" on every platform) and AZURE_CONFIG_DIR points at an empty
/// temporary folder. Shared by <see cref="SetupScriptDryRun"/> and <see cref="SetupScriptRun"/>.
/// </summary>
internal static class SetupScriptProcess
{
    internal const string FakeAzFile = "az.ps1";

    private const string WindowsPowerShell = "powershell.exe";
    private const string PowerShell7 = "pwsh";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>Creates the fake az folder and an empty Azure config folder in the work folder.</summary>
    internal static string WriteFakeAz(string workFolder, string fakeAz)
    {
        var fakeAzFolder = Directory.CreateDirectory(Path.Combine(workFolder, "bin")).FullName;
        File.WriteAllText(Path.Combine(fakeAzFolder, FakeAzFile), fakeAz);
        return fakeAzFolder;
    }

    internal static ScriptProcessResult Run(string workFolder, string fakeAzFolder,
        IReadOnlyDictionary<string, string> environment, IEnumerable<string> switches)
    {
        var azureConfigFolder = Directory.CreateDirectory(Path.Combine(workFolder, "azure-config")).FullName;
        var startInfo = StartInfo(switches);
        startInfo.Environment["PATH"] = fakeAzFolder;
        startInfo.Environment["AZURE_CONFIG_DIR"] = azureConfigFolder;
        foreach (var (name, value) in environment)
            startInfo.Environment[name] = value;
        return Execute(startInfo);
    }

    private static ProcessStartInfo StartInfo(IEnumerable<string> switches)
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

    private static ScriptProcessResult Execute(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("PowerShell did not start.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The setup script did not finish within {Timeout}.");
        }
        process.WaitForExit();
        return new ScriptProcessResult(process.ExitCode, output.Result, error.Result);
    }

    private static string ShellExecutable() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsPowerShell : PowerShell7;
}

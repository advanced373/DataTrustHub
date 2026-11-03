using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.Build.Helpers;

namespace DataTrustHub.Features.Tests.AzureSetup.Helpers;

/// <summary>The standard output, standard error and exit code of one run of the setup script.</summary>
internal sealed record ScriptProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Runs infra/setup.ps1 in a separate PowerShell process (Windows PowerShell 5.1 on Windows, pwsh elsewhere)
/// with a fake az. The real Azure CLI can never be reached: PATH holds only the given folder with the fake
/// (az.ps1, which PowerShell resolves for "az" on every platform) and AZURE_CONFIG_DIR points at an empty
/// temporary folder. Shared by <see cref="SetupScriptDryRun"/> and <see cref="SetupScriptRun"/>.
/// The environment variable DATATRUSTHUB_TEST_POWERSHELL, when set, names the PowerShell to run instead
/// (for example pwsh on Windows, to check the script under PowerShell 7 locally).
/// </summary>
internal static class SetupScriptProcess
{
    internal const string FakeAzFile = "az.ps1";

    private const string WindowsPowerShell = "powershell.exe";
    private const string PowerShell7 = "pwsh";
    private const string ShellOverrideVariable = "DATATRUSTHUB_TEST_POWERSHELL";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>Creates the fake az folder and an empty Azure config folder in the work folder.</summary>
    internal static string WriteFakeAz(string workFolder, string fakeAz)
    {
        var fakeAzFolder = Directory.CreateDirectory(Path.Combine(workFolder, "bin")).FullName;
        File.WriteAllText(Path.Combine(fakeAzFolder, FakeAzFile), fakeAz);
        return fakeAzFolder;
    }

    /// <summary>Runs infra/setup.ps1, or the script at <paramref name="scriptPath"/> when one is given.</summary>
    internal static ScriptProcessResult Run(string workFolder, string fakeAzFolder,
        IReadOnlyDictionary<string, string> environment, IEnumerable<string> switches, string? scriptPath = null)
    {
        var azureConfigFolder = Directory.CreateDirectory(Path.Combine(workFolder, "azure-config")).FullName;
        var startInfo = StartInfo(switches, scriptPath ?? Path.Combine(SolutionFiles.RepositoryRoot, SetupScriptParser.SetupScriptFile));
        startInfo.Environment["PATH"] = fakeAzFolder;
        startInfo.Environment["AZURE_CONFIG_DIR"] = azureConfigFolder;
        foreach (var (name, value) in environment)
            startInfo.Environment[name] = value;
        return Execute(startInfo);
    }

    private static ProcessStartInfo StartInfo(IEnumerable<string> switches, string scriptPath)
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
            "-File", scriptPath];
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
        Environment.GetEnvironmentVariable(ShellOverrideVariable) is { Length: > 0 } shell ? shell
        : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsPowerShell : PowerShell7;
}

/// <summary>
/// Reads an error the way the owner reads it, whatever the shell did to its layout. PowerShell renders an
/// uncaught error for the console, not as plain text: pwsh 7 adds ANSI colours and a "|" gutter and word-wraps
/// the message, Windows PowerShell 5.1 cuts lines at the buffer width even inside a word, and both widths
/// differ between Windows and Linux. So a step name can land on two lines ("managed" / "| identity").
/// Text is compared without colours, gutters and whitespace; the words themselves must all be there, in order.
/// </summary>
internal static partial class RenderedError
{
    /// <summary>Tells whether the rendered error holds the text, ignoring the console layout.</summary>
    internal static bool Contains(string renderedError, string text,
        StringComparison comparison = StringComparison.Ordinal) =>
        WithoutLayout(renderedError).Contains(WithoutLayout(text), comparison);

    private static string WithoutLayout(string text)
    {
        var plain = AnsiEscape().Replace(text, string.Empty);
        var withoutGutter = ConciseViewGutter().Replace(plain, string.Empty);
        return Whitespace().Replace(withoutGutter, string.Empty);
    }

    // A colour or other control sequence: ESC [ parameters final-letter
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscape();

    // The pwsh ConciseView gutter at the start of a line: "Line |", "  12 | ", "     | "
    [GeneratedRegex(@"^[ \t]*(Line|\d+)?[ \t]*\|", RegexOptions.Multiline)]
    private static partial Regex ConciseViewGutter();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

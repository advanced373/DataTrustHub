using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DataTrustHub.Features.Tests.Build.Helpers;

namespace DataTrustHub.Features.Tests.AzureSetup.Helpers;

/// <summary>A parameter of the setup script, as declared in its param block.</summary>
internal sealed record ScriptParameter(string Name, string Type, string? DefaultValue);

/// <summary>What the PowerShell parser says about the setup script, without running it.</summary>
internal sealed record ParsedScript(
    IReadOnlyList<string> Errors,
    IReadOnlyList<ScriptParameter> Parameters,
    IReadOnlyList<string> PowerShell7OnlyTokens);

/// <summary>
/// Parses infra/setup.ps1 with the PowerShell language parser in a separate process: Windows PowerShell 5.1
/// on Windows (the owner's shell), PowerShell 7 elsewhere (the CI runner). The script is never executed,
/// so no Azure CLI, network or sign-in is needed.
/// </summary>
internal static class SetupScriptParser
{
    internal const string SetupScriptFile = "infra/setup.ps1";

    private const string WindowsPowerShell = "powershell.exe";
    private const string PowerShell7 = "pwsh";
    private const string ScriptPathVariable = "DATATRUSTHUB_SETUP_SCRIPT";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    // Prints the parse errors, the declared parameters and any token only PowerShell 7 knows, as JSON
    private const string ParseCommand = """
        $ErrorActionPreference = 'Stop'
        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($env:DATATRUSTHUB_SETUP_SCRIPT, [ref] $tokens, [ref] $errors)
        $parameters = @()
        if ($ast.ParamBlock) {
            $parameters = @($ast.ParamBlock.Parameters | ForEach-Object {
                $default = $null
                if ($_.DefaultValue) { $default = [string] $_.DefaultValue.SafeGetValue() }
                [pscustomobject] @{ Name = $_.Name.VariablePath.UserPath; Type = $_.StaticType.Name; DefaultValue = $default }
            })
        }
        $ps7Kinds = @('QuestionQuestion', 'QuestionQuestionEquals', 'QuestionDot', 'QuestionLBracket', 'AndAnd', 'OrOr', 'QuestionMark')
        $ps7Tokens = @($tokens | Where-Object { $ps7Kinds -contains $_.Kind.ToString() } | ForEach-Object { $_.Text })
        $result = [pscustomobject] @{
            Errors = @($errors | ForEach-Object { $_.Message })
            Parameters = $parameters
            PowerShell7OnlyTokens = $ps7Tokens
        }
        ConvertTo-Json -InputObject $result -Depth 4 -Compress
        """;

    private static readonly Lazy<ParsedScript> Parsed = new(Parse);

    internal static ParsedScript Script => Parsed.Value;

    private static ParsedScript Parse()
    {
        var output = RunParser(Path.Combine(SolutionFiles.RepositoryRoot, SetupScriptFile));
        var parsed = JsonSerializer.Deserialize<ParsedScript>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return parsed ?? throw new InvalidOperationException($"The parser printed no result: {output}");
    }

    private static string RunParser(string scriptPath)
    {
        var startInfo = new ProcessStartInfo(ShellExecutable())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Encode(ParseCommand) })
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment[ScriptPathVariable] = scriptPath;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("PowerShell did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The PowerShell parser did not finish within {Timeout}.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"The PowerShell parser failed ({process.ExitCode}): {error.Result}");
        return output.Result.Trim();
    }

    private static string ShellExecutable() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsPowerShell : PowerShell7;

    // -EncodedCommand takes base64 of UTF-16LE, which avoids quoting the command for the shell
    private static string Encode(string command) => Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
}

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DataTrustHub.Features.Tests.Build.Helpers;

/// <summary>The exit code and the two output streams of a step script run by <see cref="StepScript"/>.</summary>
internal sealed record StepScriptResult(int ExitCode, string Output, string Error)
{
    internal IEnumerable<string> Lines() => (Output + "\n" + Error).Replace("\r\n", "\n").Split('\n');
}

/// <summary>
/// Runs the "run: |" script of a workflow step in bash, the way a GitHub-hosted Linux runner does, inside a
/// throwaway folder whose bin folder comes first on PATH: fake commands there (az, timeout, sleep) stand in
/// for Azure and for the clock, so nothing reaches Azure or GitHub. On Windows the bash of Git for Windows
/// runs the script.
/// </summary>
internal sealed class StepScript : IDisposable
{
    // GitHub runs a step without "shell:" as "bash -e {0}" and one with "shell: bash" with pipefail too
    private static readonly string[] DefaultShellOptions = ["--noprofile", "--norc", "-e"];
    private static readonly string[] BashShellOptions = ["--noprofile", "--norc", "-e", "-o", "pipefail"];
    private static readonly Regex LiteralRun = new(@"^(?<indent>\s*)run:\s*\|[-+]?\s*$");
    private static readonly Regex ShellLine = new(@"^\s*shell:\s*(?<shell>\S+)\s*$", RegexOptions.Multiline);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);
    private const string FakeBinVariable = "FAKE_BIN";
    private const string Prelude = "PATH=\"$" + FakeBinVariable + ":$PATH\"\n";

    private readonly Dictionary<string, string> environment = new();

    internal StepScript()
    {
        Root = Path.Combine(Path.GetTempPath(), "dth-step-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root, "bin"));
        environment[FakeBinVariable] = BashPathOf(Path.Combine(Root, "bin"));
    }

    /// <summary>The throwaway folder, as a Windows or Linux path.</summary>
    internal string Root { get; }

    /// <summary>A file in the throwaway folder, as bash sees it.</summary>
    internal string BashPath(string relativePath) => BashPathOf(Path.Combine(Root, relativePath));

    /// <summary>A file in the throwaway folder, as .NET sees it.</summary>
    internal string LocalPath(string relativePath) => Path.Combine(Root, relativePath);

    internal void Set(string variable, string value) => environment[variable] = value;

    /// <summary>A fake command on PATH: a bash script with this body.</summary>
    internal void AddCommand(string name, string body) => WriteExecutable(Path.Combine("bin", name), body);

    internal void WriteExecutable(string relativePath, string body)
    {
        var path = LocalPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ("#!/usr/bin/env bash\n" + body).Replace("\r\n", "\n"));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    internal string ReadOrEmpty(string relativePath) =>
        File.Exists(LocalPath(relativePath)) ? File.ReadAllText(LocalPath(relativePath)).Replace("\r\n", "\n") : string.Empty;

    /// <summary>Runs the step's script with the shell options GitHub would use for it.</summary>
    internal StepScriptResult Run(WorkflowStep step)
    {
        File.WriteAllText(LocalPath("step.sh"), Prelude + ScriptOf(step));
        var start = new ProcessStartInfo(BashExecutable())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var option in ShellOptionsOf(step))
            start.ArgumentList.Add(option);
        start.ArgumentList.Add(BashPath("step.sh"));
        foreach (var (variable, value) in environment)
            start.Environment[variable] = value;

        return Execute(start);
    }

    /// <summary>The body of the step's literal "run: |" block, without its indentation.</summary>
    internal static string ScriptOf(WorkflowStep step)
    {
        var lines = step.Text.Split('\n');
        var start = Array.FindIndex(lines, line => LiteralRun.IsMatch(line));
        if (start < 0)
            throw new InvalidOperationException($"Step '{step.Name}' has no literal run: | block.");

        var runIndent = LiteralRun.Match(lines[start]).Groups["indent"].Value.Length;
        var body = lines.Skip(start + 1)
            .TakeWhile(line => line.Trim().Length == 0 || Indent(line) > runIndent)
            .ToList();
        var bodyIndent = body.Where(line => line.Trim().Length > 0).Min(Indent);
        return string.Join('\n', body.Select(line => line.Length >= bodyIndent ? line[bodyIndent..] : string.Empty)) + "\n";
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { /* a leftover temp folder does not change a test result */ }
        catch (UnauthorizedAccessException) { /* same */ }
    }

    private static IReadOnlyList<string> ShellOptionsOf(WorkflowStep step)
    {
        var shell = ShellLine.Match(step.Text);
        if (!shell.Success)
            return DefaultShellOptions;
        if (shell.Groups["shell"].Value == "bash")
            return BashShellOptions;
        throw new InvalidOperationException($"Step '{step.Name}' runs in {shell.Groups["shell"].Value}, not bash.");
    }

    private static StepScriptResult Execute(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("bash did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(RunTimeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"The step script did not end within {RunTimeout}.");
        }

        return new StepScriptResult(process.ExitCode, output.Result, error.Result);
    }

    // GitHub-hosted Linux runners have bash on PATH; on Windows, the bash of Git for Windows (not WSL's)
    private static string BashExecutable()
    {
        if (!OperatingSystem.IsWindows())
            return "bash";

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var gitBash = Path.Combine(programFiles, "Git", "bin", "bash.exe");
        return File.Exists(gitBash)
            ? gitBash
            : throw new InvalidOperationException($"These tests run workflow scripts in bash; install Git for Windows ({gitBash}).");
    }

    // C:\a\b becomes /c/a/b for the bash of Git for Windows
    private static string BashPathOf(string path) =>
        OperatingSystem.IsWindows() && path.Length > 1 && path[1] == ':'
            ? "/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/')
            : path;

    private static int Indent(string line) => line.Length - line.TrimStart().Length;
}

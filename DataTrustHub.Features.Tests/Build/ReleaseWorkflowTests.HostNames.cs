using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.Build.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Build;

// #154: az webapp deploy prints the app's URL on stderr, so the host names are read and masked once, right after
// the sign-in and before any step that could print them; the smoke check reuses them. The migrate loop never
// starts an attempt with under one second left (timeout 0 means no limit). The step scripts run in bash with
// fake az, timeout, sleep and migration bundle commands: nothing reaches Azure or GitHub.
public partial class ReleaseWorkflowTests
{
    private const string DefaultHostName = "defaultHostName";
    private const string GithubOutput = "GITHUB_OUTPUT";
    private const string GithubEnvFile = "github_env";
    private const string GithubOutputFile = "github_output";
    private const string IssueReference = "#154";
    private const string FakeApiHostVariable = "FAKE_API_HOST";
    private const string FakeWebAppHostVariable = "FAKE_WEBAPP_HOST";
    private const string FakeApiHost = "fake-api-154.example.test";
    private const string FakeWebAppHost = "fake-webapp-154.example.test";
    private const string FailingRead = "!fail";
    private const string FakeKillAfterSeconds = "7";
    private const string TimeoutLogFile = "timeout.log";
    private const string SleepLogFile = "sleep.log";
    private const string BundleCountFile = "bundle.count";
    private const string BundleFile = "artifacts/migration-bundle/efbundle";
    private const string NeverSucceeds = "0";
    private const string MigrationsFailed = "::error::The migrations failed after";
    private const string MigrationsFailedAfterOneAttempt =
        "::error::The migrations failed after 1 attempt(s); see infra/README.md, step 5";
    private const int MinimumAttemptSeconds = 1;

    private static readonly Regex AzureCli = new(@"\baz\s+[a-z]");
    // A command or name that reads or shows an app's host names
    private static readonly Regex HostReading = new(
        @"defaultHostName|hostNames|az\s+webapp\s+(show|list|browse)\b|az\s+webapp\s+config\s+hostname|azurewebsites\.net",
        RegexOptions.IgnoreCase);
    // name="$(az webapp show ...)": the output goes into a shell variable, not to the log
    private static readonly Regex HostRead = new(@"^\s*(?<variable>\w+)=""?\$\(\s*az\s+webapp\s+show\b(?<arguments>[^\n]*)\)""?\s*$");
    private static readonly Regex EmptinessTest = new(@"^\s*(if\s+)?(\[\[?|test)\s+-[zn]\s");
    private static readonly Regex GithubFileWrite = new(@">>\s*""?\$\{?(GITHUB_ENV|GITHUB_OUTPUT)\b");
    private static readonly Regex ExportLine = new(@"^(?<key>\w+)=(?<value>.*)$", RegexOptions.Multiline);

    // AC2, AC3 — one step reads the host names; the smoke check does not read them again

    [Fact]
    public void HostNames_AreReadByOneStepOnly()
    {
        var reading = Workflow().JobNames().SelectMany(job => Workflow().Steps(job))
            .Where(step => HostReading.IsMatch(CodeOf(step.Text)))
            .Select(step => step.Name).ToList();

        Assert.Single(reading);
        Assert.DoesNotMatch(HostReading, CodeOf(SmokeStep().Text));
    }

    // AC1 — after the sign-in, before the migration, the deploys and the smoke check

    [Fact]
    public void HostMaskStep_RunsAfterTheSignIn_BeforeTheMigrationTheDeploysAndTheSmokeCheck()
    {
        var steps = ReleaseSteps();
        var mask = steps.IndexOf(HostMaskStep());

        Assert.True(IndexOf(steps, AzureLoginAction) < mask, "The host names are read before the sign-in.");
        Assert.All(new[] { MigrateStep(), ApiDeployStep(), WebAppDeployStep(), SmokeStep() },
            step => Assert.True(mask < steps.IndexOf(step), $"'{step.Name}' runs before the host names are masked."));
    }

    // AC3 — before the masking step, no step runs the Azure CLI, so none can print a host name
    [Fact]
    public void StepsBeforeTheHostMaskStep_RunNoAzureCli()
    {
        var steps = ReleaseSteps();
        var before = steps.Take(steps.IndexOf(HostMaskStep()))
            .Where(step => AzureCli.IsMatch(CodeOf(step.Text)))
            .Select(step => step.Name);

        Assert.Empty(before);
    }

    [Theory]
    [InlineData(ApiAppName, ApiKeyHint)]
    [InlineData(WebAppAppName, WebAppKeyHint)]
    public void HostMaskStep_ReadsTheHostNameWithAzWebAppShowAsTsv(string appName, string keyHint)
    {
        var appVariable = new Regex($@"\$\{{?{EnvKeyOf(appName, keyHint)}\b");
        var read = HostReads().SingleOrDefault(candidate => appVariable.IsMatch(candidate.Arguments));

        Assert.True(read is not null, $"No line reads the host name of {appName} into a shell variable with az webapp show.");
        Assert.Matches(@"--query\s+""?defaultHostName""?(\s|$)", read!.Arguments);
        Assert.Matches(@"(-o|--output)\s+tsv\b", read!.Arguments);
    }

    // Reading a variable to test that it is empty prints nothing; any other use must come after its ::add-mask::
    [Fact]
    public void HostMaskStep_MasksEachHostNameBeforeAnyOtherUse()
    {
        var reads = HostReads();
        var code = CodeLines(HostMaskStep());

        Assert.Equal(2, reads.Count);
        Assert.All(reads, read =>
        {
            var uses = code.Skip(read.LineIndex + 1)
                .Where(line => UsesVariable(line, read.Variable) && !EmptinessTest.IsMatch(line)).ToList();
            Assert.NotEmpty(uses);
            Assert.Contains(AddMask + "$", uses[0].Replace("${", "$"));
        });
    }

    [Fact]
    public void HostMaskStep_PrintsTheHostNamesOnlyToMaskThem()
    {
        var code = CodeLines(HostMaskStep());
        var reads = HostReads();
        var printing = code.Where(line => reads.Any(read => UsesVariable(line, read.Variable)))
            .Where(line => !HostRead.IsMatch(line) && !EmptinessTest.IsMatch(line)
                && !line.Contains(AddMask) && !GithubFileWrite.IsMatch(line));

        Assert.Empty(printing);
        Assert.DoesNotMatch(@"\bset\s+-[a-z]*x", string.Join('\n', code));
    }

    [Fact]
    public void HostMaskStep_WithBothHostNames_PrintsThemOnlyInAddMaskCommands()
    {
        using var script = HostMaskScript(FakeApiHost, FakeWebAppHost);
        var result = script.Run(HostMaskStep());

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.All(new[] { FakeApiHost, FakeWebAppHost }, host =>
        {
            Assert.Contains(AddMask + host, result.Lines().Select(line => line.Trim()));
            Assert.All(result.Lines().Where(line => line.Contains(host)), line => Assert.StartsWith(AddMask, line.Trim()));
        });
    }

    // No unmasked fallback: an empty or failed read fails the step
    [Theory]
    [InlineData(FakeApiHostVariable, "")]
    [InlineData(FakeWebAppHostVariable, "")]
    [InlineData(FakeApiHostVariable, FailingRead)]
    [InlineData(FakeWebAppHostVariable, FailingRead)]
    public void HostMaskStep_WhenAReadIsEmptyOrFails_FailsTheStep(string hostVariable, string readResult)
    {
        using var script = HostMaskScript(FakeApiHost, FakeWebAppHost);
        script.Set(hostVariable, readResult);

        var result = script.Run(HostMaskStep());

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(Exports(script), export => export.Value.Length == 0);
        Assert.All(result.Lines().Where(line => line.Contains(FakeApiHost) || line.Contains(FakeWebAppHost)),
            line => Assert.StartsWith(AddMask, line.Trim()));
    }

    // AC2 — the smoke check probes the values the masking step exported ($GITHUB_ENV or step outputs)
    [Fact]
    public void SmokeCheck_ReusesTheHostNamesOfTheHostMaskStep()
    {
        using var script = HostMaskScript(FakeApiHost, FakeWebAppHost);
        var result = script.Run(HostMaskStep());
        var exports = Exports(script);
        var smoke = CodeOf(SmokeStep().Text);

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.All(new[] { FakeApiHost, FakeWebAppHost }, host =>
        {
            var carrying = exports.Where(export => export.Value == host).ToList();
            Assert.NotEmpty(carrying);
            Assert.Contains(carrying, export => ReadsExport(smoke, export));
        });
    }

    // AC4 — the migrate loop; the budget, the pause, the kill-after and the messages are as before

    [Fact]
    public void MigrateLoop_WhenTheFirstAttemptSucceeds_GivesItTheBudgetAndTheKillAfter()
    {
        using var script = MigrateScript(budgetSeconds: 30, pauseSeconds: 20, succeedsAtAttempt: "1");
        var result = script.Run(MigrateStep());
        var attempts = TimeoutCalls(script);

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Contains("Migrations applied after 1 attempt(s)", result.Output);
        Assert.Single(attempts);
        Assert.InRange(attempts[0].Seconds, 29, 30);
        Assert.Equal(FakeKillAfterSeconds, attempts[0].KillAfter);
    }

    [Fact]
    public void MigrateLoop_AfterAFailedAttempt_PausesAndTriesAgain()
    {
        using var script = MigrateScript(budgetSeconds: 30, pauseSeconds: 5, succeedsAtAttempt: "2");
        var result = script.Run(MigrateStep());

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Contains("Attempt 1 failed; trying again in 5 s", result.Output);
        Assert.Equal(new[] { "5" }, SleepCalls(script));
        Assert.Contains("Migrations applied after 2 attempt(s)", result.Output);
        Assert.All(TimeoutCalls(script), attempt => Assert.True(attempt.Seconds >= MinimumAttemptSeconds));
    }

    [Fact]
    public void MigrateLoop_WhenThePauseWouldReachTheDeadline_FailsWithoutPausing()
    {
        using var script = MigrateScript(budgetSeconds: 5, pauseSeconds: 10, succeedsAtAttempt: NeverSucceeds);
        var result = script.Run(MigrateStep());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(MigrationsFailedAfterOneAttempt, result.Output);
        Assert.Empty(SleepCalls(script));
        Assert.Single(TimeoutCalls(script));
    }

    // The pause overruns (the fake sleep takes 3 s for a pause of 0 s), so under one second is left after it:
    // the loop must fail instead of starting timeout with 0 (no limit) or a negative duration
    [Fact]
    public void MigrateLoop_WhenUnderASecondIsLeftAfterThePause_FailsInsteadOfStartingAnAttempt()
    {
        using var script = MigrateScript(budgetSeconds: 2, pauseSeconds: 0, succeedsAtAttempt: NeverSucceeds);
        script.Set("FAKE_SLEEP_SECONDS", "3");

        var result = script.Run(MigrateStep());
        var attempts = TimeoutCalls(script);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(MigrationsFailedAfterOneAttempt, result.Output);
        Assert.Single(attempts);
        Assert.All(attempts, attempt => Assert.True(attempt.Seconds >= MinimumAttemptSeconds, $"timeout {attempt.Seconds}"));
    }

    [Fact]
    public void MigrateLoop_WhenTheBudgetIsUnderASecond_StartsNoAttempt()
    {
        using var script = MigrateScript(budgetSeconds: 0, pauseSeconds: 0, succeedsAtAttempt: "1");
        var result = script.Run(MigrateStep());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(MigrationsFailed, result.Output);
        Assert.Empty(TimeoutCalls(script));
    }

    // AC6 — documented

    [Fact]
    public void HostMaskStep_Comment_ExplainsTheMaskingBeforeTheDeploys()
    {
        var comment = Workflow().CommentAbove(HostMaskStep().Name);

        Assert.Contains(IssueReference, comment);
        Assert.Contains("mask", comment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deploy", comment, StringComparison.OrdinalIgnoreCase);
    }

    // Helpers

    private sealed record HostReadLine(string Variable, string Arguments, int LineIndex);

    private sealed record Export(string File, string Key, string Value);

    private sealed record TimeoutCall(int Seconds, string KillAfter);

    private static List<string> CodeLines(WorkflowStep step) => CodeOf(step.Text).Split('\n').ToList();

    private static List<HostReadLine> HostReads() =>
        CodeLines(HostMaskStep())
            .Select((line, index) => (Match: HostRead.Match(line), Index: index))
            .Where(item => item.Match.Success)
            .Select(item => new HostReadLine(item.Match.Groups["variable"].Value, item.Match.Groups["arguments"].Value, item.Index))
            .ToList();

    private static bool UsesVariable(string line, string variable) => Regex.IsMatch(line, $@"\$\{{?{variable}\b");

    // An exported variable is read as $KEY or env.KEY; a step output as outputs.KEY
    private static bool ReadsExport(string code, Export export) =>
        export.File == GithubEnvFile
            ? Regex.IsMatch(code, $@"(\$\{{?|env\.){export.Key}\b")
            : Regex.IsMatch(code, $@"outputs\.{export.Key}\b");

    private static List<Export> Exports(StepScript script) =>
        new[] { GithubEnvFile, GithubOutputFile }
            .SelectMany(file => ExportLine.Matches(script.ReadOrEmpty(file))
                .Select(match => new Export(file, match.Groups["key"].Value, match.Groups["value"].Value.TrimEnd('\r'))))
            .ToList();

    private static StepScript HostMaskScript(string apiHost, string webAppHost)
    {
        var script = WorkflowEnvScript();
        script.Set(GithubEnv, script.BashPath(GithubEnvFile));
        script.Set(GithubOutput, script.BashPath(GithubOutputFile));
        script.Set(FakeApiHostVariable, apiHost);
        script.Set(FakeWebAppHostVariable, webAppHost);
        script.AddCommand("az", FakeAz());
        return script;
    }

    private static StepScript MigrateScript(int budgetSeconds, int pauseSeconds, string succeedsAtAttempt)
    {
        var script = WorkflowEnvScript();
        script.Set("RETRY_BUDGET_SECONDS", budgetSeconds.ToString());
        script.Set("MIGRATE_PAUSE_SECONDS", pauseSeconds.ToString());
        script.Set("MIGRATE_KILL_AFTER_SECONDS", FakeKillAfterSeconds);
        script.Set("ARTIFACTS_DIR", script.BashPath("artifacts"));
        script.Set("SQL_HOST", "fake-sql-154");
        script.Set("FAKE_TIMEOUT_LOG", script.BashPath(TimeoutLogFile));
        script.Set("FAKE_SLEEP_LOG", script.BashPath(SleepLogFile));
        script.Set("FAKE_BUNDLE_COUNT", script.BashPath(BundleCountFile));
        script.Set("FAKE_BUNDLE_SUCCEEDS_AT", succeedsAtAttempt);
        script.WriteExecutable(BundleFile, FakeBundle);
        script.AddCommand("timeout", FakeTimeout);
        script.AddCommand("sleep", FakeSleep);
        return script;
    }

    // The workflow-level and job-level env, as the runner would set it
    private static StepScript WorkflowEnvScript()
    {
        var script = new StepScript();
        foreach (var entry in EnvEntries())
            script.Set(entry.Groups["key"].Value, entry.Groups["value"].Value);
        return script;
    }

    private static List<TimeoutCall> TimeoutCalls(StepScript script) =>
        script.ReadOrEmpty(TimeoutLogFile).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' '))
            .Select(parts => new TimeoutCall(int.Parse(parts[0]), parts.Length > 1 ? parts[1] : string.Empty))
            .ToList();

    private static List<string> SleepCalls(StepScript script) =>
        script.ReadOrEmpty(SleepLogFile).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

    // az webapp show --name <app> ... prints the fake host of that app, or fails for FailingRead
    private static string FakeAz() => $$"""
        name=""
        previous=""
        for argument in "$@"; do
          if [ "$previous" = "--name" ] || [ "$previous" = "-n" ]; then name="$argument"; fi
          previous="$argument"
        done
        case "$name" in
          "${{{EnvKeyOf(ApiAppName, ApiKeyHint)}}}") host="${{FakeApiHostVariable}}" ;;
          "${{{EnvKeyOf(WebAppAppName, WebAppKeyHint)}}}") host="${{FakeWebAppHostVariable}}" ;;
          *) echo "fake az: unexpected call" >&2; exit 2 ;;
        esac
        if [ "$host" = "{{FailingRead}}" ]; then echo "ERROR: (ResourceNotFound) fake read failure" >&2; exit 3; fi
        printf '%s\n' "$host"
        """;

    // Records "<duration> <kill-after>" and runs the command without a limit
    private const string FakeTimeout = """
        kill_after=""
        while [ $# -gt 0 ]; do
          case "$1" in
            --kill-after=*) kill_after="${1#--kill-after=}"; shift ;;
            -k|--kill-after|-s|--signal) [ "$1" = "-k" ] || [ "$1" = "--kill-after" ] && kill_after="$2"; shift 2 ;;
            --*|-v) shift ;;
            *) break ;;
          esac
        done
        echo "$1 $kill_after" >> "$FAKE_TIMEOUT_LOG"
        shift
        exec "$@"
        """;

    // Records the pause; really waits only FAKE_SLEEP_SECONDS, to simulate a pause that overruns
    private const string FakeSleep = """
        echo "$1" >> "$FAKE_SLEEP_LOG"
        if [ "${FAKE_SLEEP_SECONDS:-0}" != "0" ]; then command -p sleep "$FAKE_SLEEP_SECONDS"; fi
        """;

    // Fails until attempt FAKE_BUNDLE_SUCCEEDS_AT (0: always fails)
    private const string FakeBundle = """
        count=$(( $(cat "$FAKE_BUNDLE_COUNT" 2>/dev/null || echo 0) + 1 ))
        echo "$count" > "$FAKE_BUNDLE_COUNT"
        [ "$FAKE_BUNDLE_SUCCEEDS_AT" -gt 0 ] && [ "$count" -ge "$FAKE_BUNDLE_SUCCEEDS_AT" ]
        """;
}

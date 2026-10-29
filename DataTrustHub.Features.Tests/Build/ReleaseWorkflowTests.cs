using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.Build.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Build;

// #102: the release workflow builds once without Azure access, waits for the owner's approval in the
// environment "production", signs in through OIDC, migrates, deploys the API then the WebApp and checks
// /health. Read as text (the test project has no YAML library); what only a real run proves is not here.
public class ReleaseWorkflowTests
{
    private const string ReleaseWorkflowFile = ".github/workflows/release.yml";
    private const string InfraReadmeFile = "infra/README.md";
    private const string FirstReleaseHeading = "### 5. The first release";
    private const string Net10Sdk = "10.0.x";
    private const string ProductionEnvironment = "production";
    private const string ResourceGroupName = "DataTrustHub";
    private const string ApiAppName = "datatrusthubapi";
    private const string WebAppAppName = "DataTrustHub";
    private const string DatabaseName = "datatrusthub";
    private const string HealthPath = "/health";
    private const string HealthyStatus = "200";
    private const string AddMask = "::add-mask::";
    private const string GithubEnv = "GITHUB_ENV";
    private const string JwtSecretExport = "Jwt__Secret=";
    private const string AzureLoginAction = "azure/login@";
    private const string WebAppsDeployAction = "azure/webapps-deploy@";
    private const string AzWebAppDeploy = "az webapp deploy";
    private const string UploadArtifactAction = "actions/upload-artifact@";
    private const string DownloadArtifactAction = "actions/download-artifact@";
    private const string SetupDotnetAction = "actions/setup-dotnet@";
    private const string MigrateStepKey = "migrate";
    private const string SmokeStepKey = "smoke";
    private const int MinimumConnectTimeoutSeconds = 30;
    private const int MinimumRetryBudgetSeconds = 180;
    private const int MaximumRetryBudgetSeconds = 240;

    // Hints that pick the workflow-level env key of a resource name (the WebApp and the group share a value)
    private const string ResourceGroupKeyHint = "GROUP";
    private const string ApiKeyHint = "API";
    private const string WebAppKeyHint = "WEBAPP";
    private const string DatabaseKeyHint = "DATABASE";

    private static readonly Regex DotnetVersionLine = new(@"^\s*dotnet-version:\s*(?<version>\S+)\s*$", RegexOptions.Multiline);
    private static readonly Regex AzureUse = new(@"(\baz\s+[a-z]|azure/|vars\.AZURE_)", RegexOptions.IgnoreCase);
    private static readonly Regex EnvLine = new(@"^\s+(?<key>[A-Z_][A-Z0-9_]*):\s*(?<value>\S+)\s*$", RegexOptions.Multiline);
    private static readonly Regex ConnectTimeout = new(@"Connect Timeout=(?<seconds>\d+)", RegexOptions.IgnoreCase);
    // A deadline assigned from the elapsed time: name=$((SECONDS + BUDGET)) or name=$(( $(date +%s) + BUDGET ))
    private static readonly Regex Deadline = new(@"\w+=""?\$\(\(\s*(\$?SECONDS|\$\(date \+%s\))\s*\+\s*\$?\{?(?<budget>\w+)");
    private static readonly Regex Loop = new(@"\b(until|while|for)\b");

    // AC1 — trigger

    [Fact]
    public void ReleaseWorkflow_TriggersOnlyOnPushToMainAndByHand()
    {
        var on = Workflow().Section("on");
        var branchLines = on.Split('\n').Where(line => line.Trim().StartsWith("branches")).ToList();

        Assert.Matches(@"push:\s*\n\s+branches:\s*\[\s*main\s*\]\s*(\n|$)", on);
        Assert.Single(branchLines);
        Assert.Contains("workflow_dispatch:", on);
        Assert.DoesNotContain("pull_request", on);
    }

    // workflow_dispatch lets anyone with write access pick a branch; the first job must refuse anything but main
    [Fact]
    public void BuildJob_RunsOnlyForMain()
    {
        Assert.Contains("github.ref == 'refs/heads/main'", Workflow().JobHeader(BuildJob()));
    }

    [Fact]
    public void ReleaseWorkflow_RunsOneReleaseAtATime_NeverCancelled()
    {
        var concurrency = Workflow().Section("concurrency");

        Assert.Matches(@"group:\s*\S+", concurrency);
        Assert.Matches(@"cancel-in-progress:\s*false\b", concurrency);
    }

    // AC2 — build once

    [Fact]
    public void BuildJob_HasNoEnvironment_AndOnlyContentsRead()
    {
        Assert.DoesNotContain("environment:", Workflow().JobHeader(BuildJob()));
        Assert.Equal(new[] { "contents: read" }, Permissions(BuildJob()));
    }

    // BuildJob() also requires that exactly one job has no Azure access
    [Fact]
    public void BuildJob_HasNoAzureAccess()
    {
        Assert.Empty(CodeOf(Workflow().Job(BuildJob())).Split('\n').Where(line => AzureUse.IsMatch(line)));
    }

    [Fact]
    public void BuildJob_RunsOnUbuntu_AndEverySetupDotnetInstallsNet10()
    {
        var versions = DotnetVersionLine.Matches(Workflow().Text);

        Assert.Matches(@"runs-on:\s*ubuntu-", Workflow().JobHeader(BuildJob()));
        Assert.NotEmpty(versions);
        Assert.All(versions, match => Assert.Equal(Net10Sdk, match.Groups["version"].Value));
    }

    [Theory]
    [InlineData("dotnet restore")]
    [InlineData("dotnet build")]
    [InlineData("dotnet test DataTrustHub.Features.Tests/")]
    [InlineData("dotnet publish DataTrustHub.API/")]
    [InlineData("dotnet publish DataTrustHub/DataTrustHub.WebApp.csproj")]
    [InlineData("migrations script --idempotent")]
    [InlineData("migrations bundle")]
    public void BuildJob_Runs(string command)
    {
        Assert.Contains(Workflow().Steps(BuildJob()), step => step.Text.Contains(command));
    }

    [Fact]
    public void BuildJob_SetsUpDotnet_ThenTestsBeforePublishing()
    {
        var steps = Workflow().Steps(BuildJob());

        Assert.True(IndexOf(steps, SetupDotnetAction) < IndexOf(steps, "dotnet restore"));
        Assert.True(IndexOf(steps, "dotnet test") < IndexOf(steps, "dotnet publish"));
    }

    // Framework-dependent, as in ci.yml: a self-contained single-file bundle cannot discover Carter's assemblies
    [Fact]
    public void BuildJob_Bundle_IsFrameworkDependentForLinuxX64()
    {
        var bundle = Workflow().Steps(BuildJob()).Single(step => step.Text.Contains("migrations bundle"));

        Assert.Contains("-r linux-x64", bundle.Text);
        Assert.DoesNotContain("--self-contained", bundle.Text);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("webapp")]
    [InlineData("efbundle")]
    [InlineData(".sql")]
    public void BuildJob_UploadsTheReleaseInput(string pathPart)
    {
        var uploads = Workflow().Steps(BuildJob()).Where(step => step.Text.Contains(UploadArtifactAction));

        Assert.Contains(uploads, step => Regex.IsMatch(step.Text, $@"path:\s*\S*{Regex.Escape(pathPart)}", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void AzureJobs_NeedTheBuild_AndDownloadItsArtifacts()
    {
        Assert.All(AzureJobs(), job =>
        {
            Assert.Matches($@"needs:\s*\[?\s*{BuildJob()}\b", Workflow().JobHeader(job));
            Assert.Contains(Workflow().Steps(job), step => step.Text.Contains(DownloadArtifactAction));
        });
    }

    // ADR-0018 §2: nothing is rebuilt after the approval
    [Theory]
    [InlineData("actions/checkout@")]
    [InlineData("dotnet build")]
    [InlineData("dotnet publish")]
    [InlineData("dotnet restore")]
    [InlineData("dotnet-ef")]
    [InlineData("migrations bundle")]
    public void AzureJobs_DoNotBuild(string command)
    {
        Assert.All(AzureJobs(), job => Assert.DoesNotContain(command, CodeOf(Workflow().Job(job))));
    }

    // AC3 — approval and identity

    [Fact]
    public void AzureJobs_RunInProduction_WithOnlyIdTokenWriteAndContentsRead()
    {
        Assert.All(AzureJobs(), job =>
        {
            Assert.Matches($@"(?m)^\s+environment:\s*{ProductionEnvironment}\s*$", Workflow().JobHeader(job));
            Assert.Equal(new[] { "contents: read", "id-token: write" }, Permissions(job));
        });
    }

    [Theory]
    [InlineData("client-id", "AZURE_CLIENT_ID")]
    [InlineData("tenant-id", "AZURE_TENANT_ID")]
    [InlineData("subscription-id", "AZURE_SUBSCRIPTION_ID")]
    public void AzureLogin_ReadsTheIdFromTheEnvironmentVariable(string input, string variable)
    {
        var steps = ReleaseSteps().Where(step => step.Text.Contains(AzureLoginAction)).ToList();

        Assert.NotEmpty(steps);
        Assert.All(steps, step => Assert.Matches($@"{input}:\s*\$\{{\{{\s*vars\.{variable}\s*\}}\}}", step.Text));
    }

    [Fact]
    public void AzureJobs_SignInBeforeUsingTheAzureCli()
    {
        Assert.All(AzureJobs(), job =>
        {
            var steps = Workflow().Steps(job);
            var firstCli = steps.ToList().FindIndex(step => Regex.IsMatch(CodeOf(step.Text), @"\baz\s+[a-z]"));
            Assert.True(IndexOf(steps, AzureLoginAction) < firstCli);
        });
    }

    // No secret, client secret or publish profile (AC3); no setting or token read, no firewall rule (AC7)
    [Theory]
    [InlineData("secrets.")]
    [InlineData("client-secret")]
    [InlineData("creds:")]
    [InlineData("publish-profile")]
    [InlineData("publishing-profile")]
    [InlineData("appsettings list")]
    [InlineData("get-access-token")]
    [InlineData("firewall-rule")]
    public void ReleaseWorkflow_DoesNotUse(string forbidden)
    {
        Assert.DoesNotContain(forbidden, CodeOf(Workflow().Text), StringComparison.OrdinalIgnoreCase);
    }

    // AC4 — migrate

    [Theory]
    [InlineData("Authentication=Active Directory Default")]
    [InlineData("Encrypt=True")]
    public void Migration_ConnectionString_Contains(string setting)
    {
        Assert.Contains(setting, MigrateStep().Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Password=")]
    [InlineData("Pwd=")]
    [InlineData("User ID=")]
    public void Migration_ConnectionString_IsPasswordless(string setting)
    {
        Assert.DoesNotContain(setting, MigrateStep().Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_ConnectionString_HasAGenerousConnectTimeout()
    {
        var timeout = ConnectTimeout.Match(MigrateStep().Text);

        Assert.True(timeout.Success, "The connection string sets no Connect Timeout.");
        Assert.True(int.Parse(timeout.Groups["seconds"].Value) >= MinimumConnectTimeoutSeconds);
    }

    [Fact]
    public void Migration_TargetsTheDatabaseFromTheWorkflowEnv()
    {
        Assert.Contains($"${EnvKeyOf(DatabaseName, DatabaseKeyHint)}", MigrateStep().Text);
    }

    [Fact]
    public void AzureJobs_GenerateAThrowawayJwtSecret_MaskedBeforeItIsExported()
    {
        var step = ReleaseSteps().First(candidate => candidate.Text.Contains(JwtSecretExport) && candidate.Text.Contains(GithubEnv));

        Assert.Contains("openssl rand", step.Text);
        Assert.True(step.Text.IndexOf(AddMask) >= 0 && step.Text.IndexOf(AddMask) < step.Text.IndexOf(JwtSecretExport));
    }

    // AC4, AC5, AC6 — migrate, then the API, then the WebApp, then the smoke check
    [Fact]
    public void ReleaseSteps_MigrateDeployApiDeployWebAppThenSmokeCheck()
    {
        var steps = ReleaseSteps();
        var order = new[] { MigrateStep(), ApiDeployStep(), WebAppDeployStep(), SmokeStep() }.Select(step => steps.IndexOf(step)).ToList();

        Assert.Equal(order.Order().ToList(), order);
        Assert.Equal(order.Count, order.Distinct().Count());
    }

    // AC4, AC6 — both loops retry with a pause and give up when the time budget is spent
    [Theory]
    [InlineData(MigrateStepKey)]
    [InlineData(SmokeStepKey)]
    public void RetryLoop_PausesBetweenAttempts_AndFailsTheStep(string stepKey)
    {
        var code = CodeOf(RetryStep(stepKey).Text);

        Assert.Matches(Loop, code);
        Assert.Contains("sleep", code);
        Assert.Contains("exit 1", code);
    }

    [Theory]
    [InlineData(MigrateStepKey)]
    [InlineData(SmokeStepKey)]
    public void RetryLoop_HasATimeBudgetOfThreeToFourMinutes(string stepKey)
    {
        var budget = Deadline.Match(CodeOf(RetryStep(stepKey).Text));
        Assert.True(budget.Success, "The loop computes no deadline from the elapsed time.");

        var seconds = int.Parse(ValueOf(budget.Groups["budget"].Value));

        Assert.InRange(seconds, MinimumRetryBudgetSeconds, MaximumRetryBudgetSeconds);
    }

    // AC5 — deploy

    [Theory]
    [InlineData(ApiAppName, ApiKeyHint, "api")]
    [InlineData(WebAppAppName, WebAppKeyHint, "webapp")]
    public void Deploy_SendsThePackageToItsAppInTheResourceGroup(string appName, string keyHint, string package)
    {
        var step = DeployStep(appName, keyHint);
        var packagePath = Regex.Match(step.Text, @"(--src-path|package:)\s*""?(?<path>[^""\s]+)").Groups["path"].Value;

        Assert.Contains(package, packagePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"${EnvKeyOf(ResourceGroupName, ResourceGroupKeyHint)}", step.Text);
    }

    // The result of az webapp deploy names the app's hosts, which the public log must not show
    [Theory]
    [InlineData(ApiAppName, ApiKeyHint)]
    [InlineData(WebAppAppName, WebAppKeyHint)]
    public void Deploy_SilencesItsOutput(string appName, string keyHint)
    {
        Assert.Matches(@"(--output|-o)\s+none\b", DeployStep(appName, keyHint).Text);
    }

    // AC6 — smoke check

    [Fact]
    public void SmokeCheck_ReadsBothHostNamesFromAzure()
    {
        var shows = Regex.Matches(SmokeStep().Text, @"az webapp show[^\n]*defaultHostName");

        Assert.Equal(2, shows.Count);
        Assert.Contains(shows, show => show.Value.Contains($"${EnvKeyOf(ApiAppName, ApiKeyHint)}"));
        Assert.Contains(shows, show => show.Value.Contains($"${EnvKeyOf(WebAppAppName, WebAppKeyHint)}"));
    }

    [Fact]
    public void SmokeCheck_ProbesTheHealthPathOverHttps()
    {
        var job = AzureJobs().First(candidate => Workflow().Steps(candidate).Contains(SmokeStep()));

        Assert.Contains(HealthPath, Workflow().Job(job));
        Assert.Contains("https://", SmokeStep().Text);
    }

    // curl --fail alone accepts a redirect; only the status code itself pins 200
    [Fact]
    public void SmokeCheck_AcceptsOnlyStatus200()
    {
        var text = SmokeStep().Text;
        var statusKeys = EnvKeysWithValue(HealthyStatus);

        Assert.Matches(@"curl[^\n]*(\\\n[^\n]*)*%\{http_code\}", text);
        Assert.True(text.Contains($"\"{HealthyStatus}\"") || statusKeys.Any(key => text.Contains($"${key}")),
            "The smoke check does not compare the status with 200.");
    }

    // AC7 — names and secrets

    [Theory]
    [InlineData(ResourceGroupName, ResourceGroupKeyHint)]
    [InlineData(ApiAppName, ApiKeyHint)]
    [InlineData(WebAppAppName, WebAppKeyHint)]
    [InlineData(DatabaseName, DatabaseKeyHint)]
    public void ReleaseWorkflow_DeclaresTheResourceNameInTheWorkflowEnv(string name, string keyHint)
    {
        Assert.NotNull(EnvKeyOf(name, keyHint));
    }

    // The names are written once, in the workflow-level env, and every step reads the variable
    [Theory]
    [InlineData(ApiAppName)]
    [InlineData(WebAppAppName)]
    [InlineData(DatabaseName)]
    public void Jobs_DoNotHardCodeAResourceName(string name)
    {
        var hardCoded = Workflow().JobNames()
            .SelectMany(job => CodeOf(Workflow().Job(job)).Split('\n'))
            .Where(line => Regex.IsMatch(line, $@"(?<![\w./-]){Regex.Escape(name)}(?![\w./-])"));

        Assert.Empty(hardCoded);
    }

    [Theory]
    [InlineData(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b")] // an Azure id
    [InlineData(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")] // an e-mail address
    [InlineData(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b")] // an IP address
    [InlineData(@"\b[a-z0-9-]+\.(azurewebsites\.net|database\.windows\.net)\b")] // a host name
    public void ReleaseWorkflow_ContainsNo(string pattern)
    {
        Assert.Empty(Regex.Matches(Workflow().Text, pattern, RegexOptions.IgnoreCase));
    }

    [Fact]
    public void ReleaseWorkflow_MasksEveryGeneratedValue_BeforeExportingIt()
    {
        var generating = Workflow().JobNames().SelectMany(job => Workflow().Steps(job))
            .Where(step => step.Text.Contains("openssl rand")).ToList();

        Assert.NotEmpty(generating);
        Assert.All(generating, step =>
            Assert.True(step.Text.IndexOf(AddMask) >= 0 && step.Text.IndexOf(AddMask) < step.Text.IndexOf(GithubEnv), step.Name));
    }

    // AC8 — documented

    [Fact]
    public void ReleaseWorkflow_HeaderComment_ExplainsTheFlow()
    {
        string[] terms = ["build", ProductionEnvironment, "approval", "OIDC", "migrations", HealthPath];

        Assert.All(terms, term => Assert.Contains(term, Workflow().HeaderComment(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InfraReadme_FirstReleaseStep_ExplainsRunApproveFailAndRerun()
    {
        string[] patterns = ["release.yml", @"Review\s+deployments", "firewall", "paused", "Re-run", "workflow_dispatch|Run workflow"];

        Assert.All(patterns, pattern => Assert.Matches(new Regex(pattern, RegexOptions.IgnoreCase), FirstReleaseSection()));
    }

    // Helpers

    private static WorkflowFile Workflow() => new(SolutionFiles.ReadText(ReleaseWorkflowFile));

    private static string FirstReleaseSection()
    {
        var readme = SolutionFiles.ReadText(InfraReadmeFile).Replace("\r\n", "\n");
        var start = readme.IndexOf(FirstReleaseHeading, StringComparison.Ordinal);
        Assert.True(start >= 0, "infra/README.md has no step 5, The first release.");
        var end = readme.IndexOf("\n## ", start, StringComparison.Ordinal);
        return end < 0 ? readme[start..] : readme[start..end];
    }

    private static bool UsesAzure(string job) => CodeOf(Workflow().Job(job)).Split('\n').Any(line => AzureUse.IsMatch(line));

    private static IReadOnlyList<string> AzureJobs()
    {
        var jobs = Workflow().JobNames().Where(UsesAzure).ToList();
        Assert.NotEmpty(jobs);
        return jobs;
    }

    private static string BuildJob() => Workflow().JobNames().Single(job => !UsesAzure(job));

    // The steps of the Azure job(s), in order; the spec allows one or several, in "needs" order
    private static List<WorkflowStep> ReleaseSteps() => AzureJobs().SelectMany(job => Workflow().Steps(job)).ToList();

    private static WorkflowStep MigrateStep() =>
        ReleaseSteps().Single(step => step.Text.Contains("efbundle") && step.Text.Contains("--connection"));

    private static WorkflowStep SmokeStep() => ReleaseSteps().Single(step => step.Text.Contains("defaultHostName"));

    private static WorkflowStep RetryStep(string stepKey) => stepKey == MigrateStepKey ? MigrateStep() : SmokeStep();

    private static WorkflowStep ApiDeployStep() => DeployStep(ApiAppName, ApiKeyHint);

    private static WorkflowStep WebAppDeployStep() => DeployStep(WebAppAppName, WebAppKeyHint);

    private static WorkflowStep DeployStep(string appName, string keyHint)
    {
        var variable = EnvKeyOf(appName, keyHint);
        return ReleaseSteps().Single(step =>
            (step.Text.Contains(AzWebAppDeploy) || step.Text.Contains(WebAppsDeployAction))
            && Regex.IsMatch(step.Text, $@"(\${variable}\b|env\.{variable}\b)"));
    }

    // Every "KEY: value" line of the workflow-level env and the job-level env blocks
    private static IReadOnlyList<Match> EnvEntries() =>
        EnvLine.Matches(Workflow().Section("env"))
            .Concat(Workflow().JobNames().SelectMany(job => EnvLine.Matches(Workflow().JobHeader(job))))
            .ToList();

    private static IReadOnlyList<string> EnvKeysWithValue(string value) =>
        EnvEntries().Where(match => match.Groups["value"].Value == value).Select(match => match.Groups["key"].Value).ToList();

    // A literal number, or the value of the env variable of that name
    private static string ValueOf(string numberOrKey) =>
        numberOrKey.All(char.IsDigit) ? numberOrKey : EnvEntries().Single(match => match.Groups["key"].Value == numberOrKey).Groups["value"].Value;

    // The workflow-level env key whose value is the resource name (the hint picks between equal values)
    private static string EnvKeyOf(string value, string keyHint)
    {
        var keys = EnvLine.Matches(Workflow().Section("env"))
            .Where(match => match.Groups["value"].Value == value)
            .Select(match => match.Groups["key"].Value).ToList();
        Assert.NotEmpty(keys);
        return keys.Single(key => key.Contains(keyHint));
    }

    // The sorted "key: value" lines under the job's permissions
    private static IReadOnlyList<string> Permissions(string job)
    {
        var header = Workflow().JobHeader(job).Split('\n');
        var start = Array.FindIndex(header, line => line.Trim() == "permissions:");
        Assert.True(start >= 0, $"Job {job} sets no permissions.");
        var indent = header[start].Length - header[start].TrimStart().Length;
        return header.Skip(start + 1)
            .TakeWhile(line => line.Trim().Length > 0 && line.Length - line.TrimStart().Length > indent)
            .Select(line => line.Trim()).Order().ToList();
    }

    private static int IndexOf(IReadOnlyList<WorkflowStep> steps, string text)
    {
        var index = steps.ToList().FindIndex(step => step.Text.Contains(text));
        Assert.True(index >= 0, $"No step contains {text}.");
        return index;
    }

    // The text without comment lines, so a comment that names a command does not count as running it
    private static string CodeOf(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));
}

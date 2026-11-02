using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.AzureSetup.Helpers;
using DataTrustHub.Features.Tests.Build.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.AzureSetup;

// #147 AC5: infra/README.md documents that the admin seed is written only with -SeedAdmin and removed after
// the first seed (ADR-0022): the settings table row, the one-time procedure (seed, API start, sign-in check,
// delete), why, and how to recover. No secret or host name: see InfraFilesTests.
// #150 AC8: the web app rows, the script's steps and the Free-tier limits say the health check is skipped on
// the Free tier (ADR-0024) and applied once the plan moves to a paid tier; the "Known gap (#150)" is gone.
public class InfraReadmeTests
{
    private const string InfraReadme = "infra/README.md";
    private const string SeedAdminSwitch = "-SeedAdmin";
    private const string OverwriteAdminSeedSwitch = "OverwriteAdminSeed";
    private const string Adr0022 = "ADR-0022";
    private const string Adr0022Link = "0022-seed-the-production-admin-once.md";
    private const string SettingsTableApiRowStart = "| API |";
    private const string SeedCommand = @".\infra\setup.ps1 -SeedAdmin";
    private const string SignInCheck = "sign in";
    private const string RecoveringAccess = "Recovering access";
    private const string ExistingPasswordUnchanged = "never changes an existing user's password";
    private const string ColdStart = "cold";
    private const string ScriptStepsStart = "The script shows the subscription";
    private const string ScriptStepsEnd = "It is idempotent";

    private const string Adr0024 = "ADR-0024";
    private const string Adr0024Link = "../docs/architecture/adr/0024-no-app-service-health-check-on-the-free-tier.md";
    private const string FreeTier = "Free tier";
    private const string PaidTier = "paid tier";
    private const string HealthCheckPathPromise = "health check path `/health`";
    private const string KnownGap150 = "Known gap (#150)";
    private const string FreeTierLimitsStart = "**App Service F1:**";
    private const string FreeTierLimitsEnd = "No SLA";
    private const string HealthCheckStepStart = "5. ";
    private const string HealthCheckStepEnd = "6. ";

    private static readonly Regex Whitespace = new(@"\s+");

    private const string DeleteCommand = "az webapp config appsettings delete --name <api app> --resource-group <group> " +
        "--setting-names " + DryRunScenario.AdminSeedEmailSetting + " " + DryRunScenario.AdminSeedPasswordSetting;

    [Fact]
    public void SettingsTable_AdminSeedRow_IsWrittenOnlyWithSeedAdmin()
    {
        Assert.Contains(SeedAdminSwitch, AdminSeedRow());
    }

    [Fact]
    public void SettingsTable_AdminSeedRow_LinksAdr0022()
    {
        var row = AdminSeedRow();

        Assert.Contains(Adr0022, row);
        Assert.Contains(Adr0022Link, row);
    }

    [Fact]
    public void SettingsTable_AdminSeedRow_SaysRemovedAfterFirstSeed()
    {
        Assert.Contains("removed", AdminSeedRow(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_SeedingProcedure_RunsSetupScriptWithSeedAdmin()
    {
        Assert.Contains(SeedCommand, Readme());
    }

    [Fact]
    public void Readme_SeedingProcedure_DeletesBothAdminSeedSettings()
    {
        Assert.Contains(DeleteCommand, Readme());
    }

    // Seed first, then check the admin can sign in, then delete the settings
    [Fact]
    public void Readme_SeedingProcedure_SeedsThenChecksSignInThenDeletes()
    {
        var readme = Readme();
        var seed = readme.LastIndexOf(SeedCommand, StringComparison.Ordinal);
        var delete = readme.IndexOf(DeleteCommand, StringComparison.Ordinal);
        var signIn = readme.IndexOf(SignInCheck, seed + 1, StringComparison.OrdinalIgnoreCase);

        Assert.True(seed >= 0 && delete > seed, "The seed command must come before the delete command.");
        Assert.True(signIn > seed && signIn < delete, "The sign-in check must come between seeding and deleting.");
    }

    [Fact]
    public void Readme_SeedingProcedure_ExplainsColdStartsAndLinksAdr0022()
    {
        var procedure = SeedingProcedure();

        Assert.Contains(ColdStart, procedure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Adr0022Link, procedure);
    }

    [Fact]
    public void Readme_SeedingProcedure_ExplainsRecovery()
    {
        var procedure = SeedingProcedure();

        Assert.Contains(RecoveringAccess, procedure);
        Assert.Contains(ExistingPasswordUnchanged, procedure);
    }

    // AC5: the description of step 2 of the script matches the new behaviour
    [Fact]
    public void Readme_ScriptSteps_WriteAdminSeedOnlyWithSeedAdmin()
    {
        var readme = Readme();
        var start = readme.IndexOf(ScriptStepsStart, StringComparison.Ordinal);
        var end = readme.IndexOf(ScriptStepsEnd, start + 1, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "The list of the script's steps is missing.");

        var steps = readme[start..end];
        Assert.Contains(DryRunScenario.AdminSeedEmailSetting, steps);
        Assert.Contains(SeedAdminSwitch, steps);
    }

    [Fact]
    public void Readme_DoesNotMentionOverwriteAdminSeed()
    {
        Assert.DoesNotContain(OverwriteAdminSeedSwitch, Readme(), StringComparison.OrdinalIgnoreCase);
    }

    // #150 AC8: the two web app rows no longer promise a health check path on F1
    [Theory]
    [InlineData("| Web app, the API |")]
    [InlineData("| Web app, the WebApp |")]
    public void ResourcesTable_WebAppRow_DoesNotPromiseHealthCheckPath(string rowStart)
    {
        Assert.DoesNotContain(HealthCheckPathPromise, Row(rowStart), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("| Web app, the API |")]
    [InlineData("| Web app, the WebApp |")]
    public void ResourcesTable_WebAppRow_SaysSkippedOnFreeTierAndAppliedOnPaidTier(string rowStart)
    {
        var row = Row(rowStart);

        Assert.Contains("skipped", row, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(FreeTier, row);
        Assert.Contains(PaidTier, row);
        Assert.Contains($"[{Adr0024}]({Adr0024Link})", row);
    }

    // #150 AC8: step 5 of the script's steps says the same
    [Fact]
    public void Readme_ScriptSteps_HealthCheckSkippedOnFreeTierAndAppliedOnPaidTier()
    {
        var step = SingleSpaced(HealthCheckStep());

        Assert.Contains("health check", step, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skips", step, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(FreeTier, step);
        Assert.Contains(PaidTier, step);
        Assert.Contains($"[{Adr0024}]({Adr0024Link})", step);
    }

    [Fact]
    public void Readme_DoesNotHaveKnownGap150()
    {
        Assert.DoesNotContain(KnownGap150, Readme());
    }

    // #150 AC8: the Free-tier limits do not read as if a health check runs on F1
    [Theory]
    [InlineData("the health check reports")]
    [InlineData("cannot move traffic")]
    [InlineData("answers 404")]
    [InlineData("until the endpoint exists")]
    public void Readme_FreeTierLimits_DoNotDescribeARunningHealthCheck(string outdated)
    {
        Assert.DoesNotContain(outdated, SingleSpaced(Readme()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_FreeTierLimits_SayNoHealthCheckOnF1AndLinkAdr0024()
    {
        var readme = Readme();
        var start = readme.IndexOf(FreeTierLimitsStart, StringComparison.Ordinal);
        var end = readme.IndexOf(FreeTierLimitsEnd, start + 1, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "The App Service F1 limits are missing.");

        var limits = SingleSpaced(readme[start..end]);
        Assert.Contains("No App Service health check", limits, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Adr0024Link, limits);
    }

    private static string Row(string rowStart) =>
        Assert.Single(Readme().Split('\n'), line => line.StartsWith(rowStart, StringComparison.Ordinal));

    // Step 5 of the script's steps, up to step 6
    private static string HealthCheckStep()
    {
        var readme = Readme();
        var steps = readme.IndexOf(ScriptStepsStart, StringComparison.Ordinal);
        var start = readme.IndexOf("\n" + HealthCheckStepStart, steps, StringComparison.Ordinal);
        var end = readme.IndexOf("\n" + HealthCheckStepEnd, start + 1, StringComparison.Ordinal);
        Assert.True(steps >= 0 && start > steps && end > start, "Step 5 of the script's steps is missing.");
        return readme[start..end];
    }

    private static string Readme() => SolutionFiles.ReadText(InfraReadme);

    // The settings table row of the API's AdminSeed settings
    private static string AdminSeedRow()
    {
        var rows = Readme().Split('\n')
            .Where(line => line.StartsWith(SettingsTableApiRowStart, StringComparison.Ordinal))
            .Where(line => line.Contains(DryRunScenario.AdminSeedEmailSetting))
            .ToList();
        return Assert.Single(rows);
    }

    // From the seed command to the end of the README section holding it
    private static string SeedingProcedure()
    {
        var readme = Readme();
        var start = readme.LastIndexOf(SeedCommand, StringComparison.Ordinal);
        Assert.True(start >= 0, "The seeding procedure is missing.");
        var sectionStart = readme.LastIndexOf("\n#", start, StringComparison.Ordinal);
        var sectionEnd = readme.IndexOf("\n## ", start, StringComparison.Ordinal);
        return SingleSpaced(readme[sectionStart..(sectionEnd < 0 ? readme.Length : sectionEnd)]);
    }

    // Markdown wraps sentences across lines: compare the text with single spaces
    private static string SingleSpaced(string text) => Whitespace.Replace(text, " ");
}

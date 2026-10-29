using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.AzureSetup.Helpers;
using DataTrustHub.Features.Tests.Build.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.AzureSetup;

// #147 AC5: infra/README.md documents that the admin seed is written only with -SeedAdmin and removed after
// the first seed (ADR-0022): the settings table row, the one-time procedure (seed, API start, sign-in check,
// delete), why, and how to recover. No secret or host name: see InfraFilesTests.
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

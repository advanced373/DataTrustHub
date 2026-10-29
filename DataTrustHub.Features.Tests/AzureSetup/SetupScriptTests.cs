using System.Text;
using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.AzureSetup.Helpers;
using DataTrustHub.Features.Tests.Build.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.AzureSetup;

// #101 AC1, AC2, AC5: infra/setup.ps1 parses with no errors, is Windows PowerShell 5.1-compatible, has the
// dry-run and confirmation switches, defaults to the production resource names and uses the Azure CLI only.
// The script is parsed, never executed.
public class SetupScriptTests
{
    private const string SwitchType = "SwitchParameter";
    private const string StringType = "String";
    private const string DeleteArgument = "'delete'";
    private const string SeedAdminSwitch = "SeedAdmin";
    private const string OverwriteAdminSeedSwitch = "OverwriteAdminSeed";
    private const string Adr0022 = "ADR-0022";
    private const string DescriptionKeyword = ".DESCRIPTION";
    private const string ParameterKeyword = ".PARAMETER";
    private const string ExampleKeyword = ".EXAMPLE";

    // The comment-based help block and its keyword lines (.SYNOPSIS, .PARAMETER Name, ...)
    private static readonly Regex HelpComment = new(@"<#(?<help>.*?)#>", RegexOptions.Singleline);
    private static readonly Regex HelpKeywordLine = new(@"^[ \t]*\.[A-Z]+(?:[ \t]+\w+)?[ \t]*\r?$", RegexOptions.Multiline);

    // A cmdlet of the Az PowerShell module (Connect-AzAccount, Set-AzWebApp, ...): the script must use az only
    private static readonly Regex AzModuleCmdlet = new(@"\b[A-Z][a-z]+-Az[A-Z]\w*");

    // A function the script defines itself (Invoke-AzRead, Invoke-AzChange): not a module cmdlet
    private static readonly Regex FunctionDefinition = new(@"^\s*function\s+(?<name>[\w-]+)", RegexOptions.Multiline);

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void SetupScript_Parses_WithoutErrors()
    {
        Assert.Empty(SetupScriptParser.Script.Errors);
    }

    [Fact]
    public void SetupScript_UsesNoPowerShell7OnlyOperator()
    {
        Assert.Empty(SetupScriptParser.Script.PowerShell7OnlyTokens);
    }

    // Windows PowerShell 5.1 reads a file without a byte order mark in the ANSI code page
    [Fact]
    public void SetupScript_IsAsciiOrHasUtf8ByteOrderMark()
    {
        var bytes = File.ReadAllBytes(Path.Combine(SolutionFiles.RepositoryRoot, SetupScriptParser.SetupScriptFile));

        var hasBom = bytes.AsSpan().StartsWith(Utf8Bom);
        Assert.True(hasBom || Ascii.IsValid(bytes), "setup.ps1 has non-ASCII characters and no UTF-8 byte order mark.");
    }

    [Theory]
    [InlineData("WhatIf")]
    [InlineData("Force")]
    [InlineData("RotateJwtSecret")]
    [InlineData(SeedAdminSwitch)]
    public void SetupScript_DeclaresSwitch(string switchName)
    {
        var parameter = Assert.Single(SetupScriptParser.Script.Parameters, parameter => parameter.Name == switchName);

        Assert.Equal(SwitchType, parameter.Type);
    }

    // #147 AC2: -SeedAdmin replaces -OverwriteAdminSeed (one switch, one meaning)
    [Fact]
    public void SetupScript_DoesNotDeclareOverwriteAdminSeed()
    {
        Assert.DoesNotContain(SetupScriptParser.Script.Parameters, parameter => parameter.Name == OverwriteAdminSeedSwitch);
    }

    // #147 AC4: neither the help nor the code mentions the removed switch
    [Fact]
    public void SetupScript_DoesNotMentionOverwriteAdminSeed()
    {
        Assert.DoesNotContain(OverwriteAdminSeedSwitch, SetupScript(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetupScriptHelp_HasParameterSectionForSeedAdmin()
    {
        Assert.Matches(HelpKeyword(ParameterKeyword + " " + SeedAdminSwitch), HelpBlock());
    }

    [Fact]
    public void SetupScriptHelp_HasExampleRunningSeedAdmin()
    {
        var examples = HelpSections(ExampleKeyword);

        Assert.Contains(examples, example => example.Contains("-" + SeedAdminSwitch));
    }

    // #147 AC4: the description says the admin seed is written only on request, with -SeedAdmin (ADR-0022)
    [Theory]
    [InlineData("-" + SeedAdminSwitch)]
    [InlineData(Adr0022)]
    public void SetupScriptHelp_DescriptionMentions(string expected)
    {
        var description = Assert.Single(HelpSections(DescriptionKeyword));

        Assert.Contains(expected, description);
    }

    // The resource names are parameters, with the existing production resources as defaults
    [Theory]
    [InlineData("ResourceGroup", "DataTrustHub")]
    [InlineData("ApiAppName", "datatrusthubapi")]
    [InlineData("WebAppName", "DataTrustHub")]
    [InlineData("SqlServerName", "datatrusthub")]
    [InlineData("DatabaseName", "datatrusthub")]
    public void SetupScript_ResourceNameParameter_DefaultsToProductionResource(string parameterName, string expectedDefault)
    {
        var parameter = Assert.Single(SetupScriptParser.Script.Parameters, parameter => parameter.Name == parameterName);

        Assert.Equal(StringType, parameter.Type);
        Assert.Equal(expectedDefault, parameter.DefaultValue);
    }

    [Fact]
    public void SetupScript_UsesNoAzPowerShellModuleCmdlet()
    {
        var script = SetupScript();
        var ownFunctions = FunctionDefinition.Matches(script).Select(match => match.Groups["name"].Value).ToHashSet();

        var moduleCmdlets = AzModuleCmdlet.Matches(script)
            .Select(match => match.Value)
            .Where(name => !ownFunctions.Contains(name))
            .Distinct()
            .ToList();

        Assert.Empty(moduleCmdlets);
    }

    // Other app settings (the Application Insights ones) must survive: nothing is ever deleted
    [Fact]
    public void SetupScript_NeverRunsADeleteCommand()
    {
        Assert.DoesNotContain(DeleteArgument, SetupScript(), StringComparison.OrdinalIgnoreCase);
    }

    private static string SetupScript() => SolutionFiles.ReadText(SetupScriptParser.SetupScriptFile);

    // The comment-based help: the first <# ... #> block of the script
    private static string HelpBlock()
    {
        var match = HelpComment.Match(SetupScript());
        Assert.True(match.Success, "setup.ps1 has no comment-based help.");
        return match.Groups["help"].Value;
    }

    // The text of every help section with the given keyword, up to the next keyword
    private static List<string> HelpSections(string keyword)
    {
        var sections = HelpKeywordLine.Split(HelpBlock());
        var keywords = HelpKeywordLine.Matches(HelpBlock()).Select(match => match.Value.Trim()).ToList();
        return keywords
            .Select((line, index) => (line, text: sections[index + 1]))
            .Where(section => section.line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            .Select(section => section.text)
            .ToList();
    }

    private static Regex HelpKeyword(string keywordLine) =>
        new($@"^\s*{Regex.Escape(keywordLine)}\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
}

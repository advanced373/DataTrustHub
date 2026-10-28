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
    public void SetupScript_DeclaresSwitch(string switchName)
    {
        var parameter = Assert.Single(SetupScriptParser.Script.Parameters, parameter => parameter.Name == switchName);

        Assert.Equal(SwitchType, parameter.Type);
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
}

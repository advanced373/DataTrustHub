using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.Build.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Build;

// AC7 of #140: every setup-dotnet step of the CI workflow installs .NET 10, and no line still names .NET 9
public class CiWorkflowTests
{
    private const string Net10Sdk = "10.0.x";

    private static readonly Regex DotnetVersionLine = new(@"^\s*dotnet-version:\s*(?<version>\S+)\s*$",
        RegexOptions.Multiline);

    // ".NET 9", "net9.0" or "9.0.x", but not ".NET 90" or "19.0.x"
    private static readonly Regex Net9Mention = new(@"(\.NET 9\b|\bnet9\.0\b|\b9\.0\.x\b)", RegexOptions.IgnoreCase);

    [Fact]
    public void CiWorkflow_SetsUpADotnetSdk()
    {
        Assert.NotEmpty(DotnetVersionLine.Matches(Workflow()));
    }

    [Fact]
    public void CiWorkflow_EverySetupDotnet_InstallsNet10()
    {
        Assert.All(DotnetVersionLine.Matches(Workflow()), match =>
            Assert.Equal(Net10Sdk, match.Groups["version"].Value));
    }

    [Fact]
    public void CiWorkflow_DoesNotMentionNet9()
    {
        var lines = Workflow().Split('\n').Where(line => Net9Mention.IsMatch(line)).ToList();

        Assert.Empty(lines);
    }

    private static string Workflow() => SolutionFiles.ReadText(SolutionFiles.CiWorkflowFile);
}

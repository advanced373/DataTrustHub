using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.Build.Helpers;
using DataTrustHub.Features.Tests.Shared.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Shared;

// AC1, AC2 and AC5 of #119: the caller id rule lives in one place. The source files are read, not the
// compiled assembly, because the rule is a pattern of claim reads that reflection cannot see
public class CallerIdRuleLocationTests
{
    private const string FeaturesFolder = "DataTrustHub.Features";
    private const string TestsFolder = "DataTrustHub.Features.Tests";
    private const string SourcePattern = "*.cs";
    private const string ExtensionDeclaration = "static class ClaimsPrincipalExtensions";
    private const string EndpointFileSuffix = "Endpoint.cs";
    private const string OldHelperName = "ExtractUserId";
    private const string CallerIdCall = ".GetUserId()";
    private const string ClaimTypeGroup = "claimType";

    private static readonly string[] BuildOutputFolders = ["bin", "obj"];

    // A read of the sub or name identifier claim; comments that name the claims and code that writes them do not match
    private static readonly Regex CallerIdClaimRead = new(
        @"(FindFirst|FindFirstValue|FindAll|HasClaim)\s*\(\s*(?<claimType>JwtRegisteredClaimNames\.Sub|ClaimTypes\.NameIdentifier|""sub"")");

    // AC2: the extension is the only place that reads the caller id claims
    [Fact]
    public void FeaturesSource_ReadsCallerIdClaimsOnlyInTheExtension()
    {
        var readers = SourceFiles(FeaturesFolder).Where(file => CallerIdClaimRead.IsMatch(file.Text)).Select(file => file.Path);

        Assert.Equal([ExtensionFile().Path], readers);
    }

    // AC1: the extension reads sub and, as fallback, NameIdentifier
    [Fact]
    public void Extension_ReadsBothCallerIdClaims()
    {
        var claimTypes = CallerIdClaimRead.Matches(ExtensionFile().Text).Select(match => match.Groups[ClaimTypeGroup].Value).Distinct();

        Assert.Equal(2, claimTypes.Count());
    }

    [Fact]
    public void FeaturesSource_HasNoPrivateExtractUserIdLeft()
    {
        var offenders = SourceFiles(FeaturesFolder).Where(file => file.Text.Contains(OldHelperName)).Select(file => file.Path);

        Assert.Empty(offenders);
    }

    // Every endpoint that reads the caller id is one of the 23 rows of CallerIdEndpointTests, and the reverse.
    // Beyond the spec on purpose: it guards future endpoints, so a new endpoint that reads the caller id
    // must be added to CallerIdEndpointTests
    [Fact]
    public void EndpointsReadingTheCallerId_AreExactlyTheEndpointsCoveredByCallerIdEndpointTests()
    {
        var readers = SourceFiles(FeaturesFolder)
            .Where(file => file.Path.EndsWith(EndpointFileSuffix, StringComparison.Ordinal) && file.Text.Contains(CallerIdCall))
            .Select(file => Path.GetFileName(file.Path)[..^EndpointFileSuffix.Length])
            .Order();

        Assert.Equal(CallerIdEndpointTests<MissingCallerIdApiFactory>.EndpointNames.Order(), readers);
    }

    // AC5: test helpers may build tokens with these claims, but none reads them back with its own copy of the rule
    [Fact]
    public void TestsSource_ReadsNoCallerIdClaim()
    {
        var offenders = SourceFiles(TestsFolder).Where(file => CallerIdClaimRead.IsMatch(file.Text)).Select(file => file.Path);

        Assert.Empty(offenders);
    }

    private static (string Path, string Text) ExtensionFile() =>
        SourceFiles(FeaturesFolder).Single(file => file.Text.Contains(ExtensionDeclaration, StringComparison.Ordinal));

    private static IEnumerable<(string Path, string Text)> SourceFiles(string projectFolder)
    {
        var root = Path.Combine(SolutionFiles.RepositoryRoot, projectFolder);
        return Directory.EnumerateFiles(root, SourcePattern, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !BuildOutputFolders.Contains(path.Split('/')[0]))
            .Select(path => (path, File.ReadAllText(Path.Combine(root, path))));
    }
}

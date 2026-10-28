using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.Build.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.AzureSetup;

// #101 AC4, AC5: infra/ holds the setup script, the SQL script and the README, linked from the root README;
// the repository is public, so no file in infra/ holds a subscription or tenant id, an e-mail address, an IP
// address (other than 0.0.0.0, the Azure services rule), a web app host name or a key
public class InfraFilesTests
{
    private const string InfraFolder = "infra";
    private const string RootReadme = "README.md";
    private const string InfraReadmeLink = "(infra/README.md)";
    private const string DevelopmentSetupFile = "docs/development-setup.md";
    private const string KeyVault = "Key Vault";
    private const string AzureServicesAddress = "0.0.0.0";

    private static readonly Regex Guid = new(
        @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase);

    private static readonly Regex EmailAddress = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}");

    private static readonly Regex IpAddress = new(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");

    private static readonly Regex WebAppHostName = new(@"\.azurewebsites\.net\b", RegexOptions.IgnoreCase);

    // A long base64 run, such as a signing key or an instrumentation key
    private static readonly Regex Base64Key = new(@"[A-Za-z0-9+/]{40,}={0,2}");

    [Theory]
    [InlineData("infra/setup.ps1")]
    [InlineData("infra/sql/grant-roles.sql")]
    [InlineData("infra/README.md")]
    public void InfraFolder_HoldsFile(string relativePath)
    {
        Assert.True(File.Exists(Path.Combine(SolutionFiles.RepositoryRoot, relativePath)), $"{relativePath} is missing.");
    }

    [Fact]
    public void RootReadme_LinksTheInfraReadme()
    {
        Assert.Contains(InfraReadmeLink, SolutionFiles.ReadText(RootReadme));
    }

    // Production takes its settings from App Service app settings (ADR-0021), not from Key Vault
    [Fact]
    public void DevelopmentSetup_DoesNotMentionKeyVault()
    {
        Assert.DoesNotContain(KeyVault, SolutionFiles.ReadText(DevelopmentSetupFile), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InfraFiles_ContainNoGuid()
    {
        Assert.Empty(Offending(Guid));
    }

    [Fact]
    public void InfraFiles_ContainNoEmailAddress()
    {
        Assert.Empty(Offending(EmailAddress));
    }

    [Fact]
    public void InfraFiles_ContainNoIpAddress_OtherThanAzureServices()
    {
        Assert.Empty(Offending(IpAddress, AzureServicesAddress));
    }

    [Fact]
    public void InfraFiles_ContainNoWebAppHostName()
    {
        Assert.Empty(Offending(WebAppHostName));
    }

    [Fact]
    public void InfraFiles_ContainNoBase64Key()
    {
        Assert.Empty(Offending(Base64Key));
    }

    // Each match in a file of infra/, as "file: match", except the allowed values
    private static List<string> Offending(Regex pattern, params string[] allowed)
    {
        var infraRoot = Path.Combine(SolutionFiles.RepositoryRoot, InfraFolder);
        var files = Directory.GetFiles(infraRoot, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        return files
            .SelectMany(file => pattern.Matches(File.ReadAllText(file))
                .Select(match => match.Value)
                .Where(value => !allowed.Contains(value))
                .Select(value => $"{Path.GetRelativePath(infraRoot, file)}: {value}"))
            .ToList();
    }
}

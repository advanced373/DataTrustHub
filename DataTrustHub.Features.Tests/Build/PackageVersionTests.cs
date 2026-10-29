using DataTrustHub.Features.Tests.Build.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Build;

// AC2 of #140: the ASP.NET Core, EF Core and Microsoft.Extensions packages share one stable 10.0.x version in
// every project, the two IdentityModel token packages share one version, and nothing is a prerelease
public class PackageVersionTests
{
    private const string Net10VersionPrefix = "10.0.";
    private const char PrereleaseSeparator = '-';

    // Packages released with the platform: they must move together (Microsoft.EntityFrameworkCore covers
    // the providers, Design and InMemory; Microsoft.AspNetCore covers OpenApi, JwtBearer and Mvc.Testing)
    private static readonly string[] PlatformPackagePrefixes =
        ["Microsoft.AspNetCore.", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions."];

    private static readonly string[] TokenPackages =
        ["Microsoft.IdentityModel.Tokens", "System.IdentityModel.Tokens.Jwt"];

    [Fact]
    public void PlatformPackages_AreReferenced()
    {
        Assert.NotEmpty(PlatformPackages());
    }

    [Fact]
    public void PlatformPackages_ShareOneVersionInEveryProject()
    {
        var versions = PlatformPackages().Select(reference => reference.Version).Distinct().ToList();

        Assert.True(versions.Count == 1, Describe(PlatformPackages()));
    }

    [Fact]
    public void PlatformPackages_AreOnNet10()
    {
        Assert.All(PlatformPackages(), reference =>
            Assert.StartsWith(Net10VersionPrefix, reference.Version));
    }

    [Fact]
    public void TokenPackages_ShareOneVersionInEveryProject()
    {
        var references = SolutionFiles.PackageReferences().Where(r => TokenPackages.Contains(r.Name)).ToList();

        Assert.NotEmpty(references);
        Assert.True(references.Select(r => r.Version).Distinct().Count() == 1, Describe(references));
    }

    [Fact]
    public void PackageReferences_HaveAnExplicitStableVersion()
    {
        Assert.All(SolutionFiles.PackageReferences(), reference =>
        {
            Assert.False(string.IsNullOrWhiteSpace(reference.Version), $"{reference.Name} in {reference.Project}");
            Assert.DoesNotContain(PrereleaseSeparator, reference.Version);
        });
    }

    internal static IReadOnlyList<PackageReference> PlatformPackages() =>
        SolutionFiles.PackageReferences()
            .Where(reference => PlatformPackagePrefixes.Any(prefix =>
                reference.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

    private static string Describe(IEnumerable<PackageReference> references) =>
        string.Join(Environment.NewLine, references.Select(r => $"{r.Project}: {r.Name} {r.Version}"));
}

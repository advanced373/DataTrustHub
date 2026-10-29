using System.Text.Json;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.Configuration;

// AC1: the committed appsettings*.json of the API and the WebApp hold no secret and no machine-specific
// connection (ADR-0007); the non-secret Jwt settings stay
public class CommittedSettingsTests
{
    private const string SolutionFile = "DataTrustHub.sln";
    private const string ApiProjectFolder = "DataTrustHub.API";
    private const string WebAppProjectFolder = "DataTrustHub";
    private const string ApiSettingsFile = "appsettings.json";
    private const string SettingsPattern = "appsettings*.json";

    private const string ConnectionStringsSection = "ConnectionStrings";
    private const string JwtSection = "Jwt";
    private const string SecretProperty = "Secret";
    private const string IssuerProperty = "Issuer";
    private const string AudienceProperty = "Audience";
    private const string ExpiryMinutesProperty = "ExpiryMinutes";
    private const int ExpectedExpiryMinutes = 60;

    // Property names that would hold a secret or a connection, matched case-insensitively
    private static readonly string[] SecretMarkers = ["Secret", "Password", "ConnectionString", "Key"];

    [Fact]
    public void ApiSettings_HaveNoConnectionStringsSection()
    {
        using var settings = ReadApiSettings();

        Assert.False(settings.RootElement.TryGetProperty(ConnectionStringsSection, out _));
    }

    [Fact]
    public void ApiSettings_JwtSectionHasNoSecret()
    {
        using var settings = ReadApiSettings();

        Assert.False(settings.RootElement.GetProperty(JwtSection).TryGetProperty(SecretProperty, out _));
    }

    [Fact]
    public void ApiSettings_JwtIssuerAudienceAndExpiry_Stay()
    {
        using var settings = ReadApiSettings();
        var jwt = settings.RootElement.GetProperty(JwtSection);

        Assert.Equal(TestJwt.Issuer, jwt.GetProperty(IssuerProperty).GetString());
        Assert.Equal(TestJwt.Audience, jwt.GetProperty(AudienceProperty).GetString());
        Assert.Equal(ExpectedExpiryMinutes, jwt.GetProperty(ExpiryMinutesProperty).GetInt32());
    }

    [Theory]
    [InlineData(ApiProjectFolder)]
    [InlineData(WebAppProjectFolder)]
    public void CommittedSettings_OfProject_HoldNoSecretOrConnection(string projectFolder)
    {
        var files = Directory.GetFiles(Path.Combine(FindRepositoryRoot(), projectFolder), SettingsPattern);
        Assert.NotEmpty(files);

        var offending = files
            .SelectMany(file => SecretPropertyPaths(file).Select(path => $"{Path.GetFileName(file)}: {path}"))
            .ToList();

        Assert.Empty(offending);
    }

    private static JsonDocument ReadApiSettings() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), ApiProjectFolder, ApiSettingsFile)));

    private static IEnumerable<string> SecretPropertyPaths(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        return CollectSecretPaths(document.RootElement, string.Empty).ToList();
    }

    private static IEnumerable<string> CollectSecretPaths(JsonElement element, string prefix)
    {
        if (element.ValueKind != JsonValueKind.Object) yield break;

        foreach (var property in element.EnumerateObject())
        {
            var path = prefix.Length == 0 ? property.Name : $"{prefix}:{property.Name}";
            if (SecretMarkers.Any(marker => property.Name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                yield return path;

            foreach (var nested in CollectSecretPaths(property.Value, path))
                yield return nested;
        }
    }

    // The source files, not the copies in bin: walk up from the test output to the solution folder
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException($"{SolutionFile} not found above {AppContext.BaseDirectory}");
    }
}

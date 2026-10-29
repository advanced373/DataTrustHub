using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DataTrustHub.Features.Tests.Build.Helpers;

/// <summary>A package reference as written in a project file of the solution.</summary>
internal sealed record PackageReference(string Project, string Name, string Version);

/// <summary>
/// Reads the source files of the repository (the solution, its project files, the tool manifest, the CI
/// workflow), not the copies in bin: the root is the first folder above the test output with the solution.
/// </summary>
internal static class SolutionFiles
{
    internal const string SolutionFile = "DataTrustHub.sln";
    internal const string ToolManifestFile = ".config/dotnet-tools.json";
    internal const string CiWorkflowFile = ".github/workflows/ci.yml";

    private const string TargetFrameworkElement = "TargetFramework";
    private const string TargetFrameworksElement = "TargetFrameworks";
    private const string PackageReferenceElement = "PackageReference";
    private const string IncludeAttribute = "Include";
    private const string VersionAttribute = "Version";

    // A project line of the .sln: Project("{type}") = "Name", "Folder\Name.csproj", "{id}"
    private static readonly Regex ProjectLine = new(@"^Project\(""\{[^}]+\}""\) = ""[^""]+"", ""(?<path>[^""]+\.csproj)""",
        RegexOptions.Multiline);

    internal static string RepositoryRoot { get; } = FindRepositoryRoot();

    internal static string ReadText(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));

    /// <summary>The project files listed in the solution, relative to the repository root.</summary>
    internal static IReadOnlyList<string> ProjectFiles() =>
        ProjectLine.Matches(ReadText(SolutionFile))
            .Select(match => match.Groups["path"].Value.Replace('\\', '/'))
            .ToList();

    /// <summary>Every TargetFramework and TargetFrameworks value of the project file.</summary>
    internal static IReadOnlyList<string> TargetFrameworks(string projectFile) =>
        Load(projectFile).Descendants()
            .Where(element => element.Name.LocalName is TargetFrameworkElement or TargetFrameworksElement)
            .Select(element => element.Value.Trim())
            .ToList();

    internal static IReadOnlyList<PackageReference> PackageReferences() =>
        ProjectFiles().SelectMany(PackageReferencesOf).ToList();

    private static IEnumerable<PackageReference> PackageReferencesOf(string projectFile) =>
        Load(projectFile).Descendants()
            .Where(element => element.Name.LocalName == PackageReferenceElement)
            .Select(element => new PackageReference(
                projectFile,
                (string?)element.Attribute(IncludeAttribute) ?? string.Empty,
                (string?)element.Attribute(VersionAttribute) ?? string.Empty));

    private static XDocument Load(string projectFile) =>
        XDocument.Load(Path.Combine(RepositoryRoot, projectFile));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException($"{SolutionFile} not found above {AppContext.BaseDirectory}");
    }
}

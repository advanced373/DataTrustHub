using System.Reflection;
using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.Build.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// #74 (ADR-0012 step 4, part 4e, AC6): with the audit trails on the read side, no handler in Features reads DContext.
// Project-wide, in the assembly (IL) and in the source: no type in DataTrustHub.Features references DContext or a
// persistence model (Db*), and no source file names the DataTrustHub.Infrastructure.Persistance namespace.
// #74 (ADR-0012 step 5, AC1): the layering rule for the whole project. No type in DataTrustHub.Features depends on
// DataTrustHub.Infrastructure (any namespace) or EF Core, and no source file names either; it replaces the per-slice
// and per-PR copies. The project reference from Features to Infrastructure stays for the DI wiring (ADR-0012 §1)
public class FeaturesPersistenceIsolationTests
{
    private const string PersistenceNamespace = InfrastructureNamespace + NamespaceSeparator + "Persistance";
    private const string PersistenceModelPrefix = "Db";
    private const string FeaturesProjectFolder = "DataTrustHub.Features";
    private const string SourcePattern = "*.cs";

    // Build output, not source
    private static readonly string[] GeneratedFolders = ["bin", "obj"];

    // A non-comment line naming the Infrastructure layer (any namespace) or EF Core
    private static readonly Regex InfrastructureOrEntityFramework =
        new(@"DataTrustHub\.Infrastructure|Microsoft\.EntityFrameworkCore", RegexOptions.CultureInvariant);

    // A non-comment line naming the context or a persistence model
    private static readonly Regex ContextOrPersistenceModel =
        new(@"\bDContext\b|\bDb[A-Z]\w*", RegexOptions.CultureInvariant);

    [Fact]
    public void FeaturesTypes_ShouldNotDependOnInfrastructureOrEntityFramework()
    {
        Assert.NotEmpty(Types.InAssembly(FeaturesAssembly).GetTypes());

        var result = Types.InAssembly(FeaturesAssembly)
            .ShouldNot().HaveDependencyOnAny(InfrastructureNamespace, EntityFrameworkNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, "No type in Features may depend on Infrastructure or EF Core");
    }

    // Reads the source too: an unused using directive or an inlined constant leaves no trace in the assembly
    [Fact]
    public void FeaturesSourceFiles_DoNotNameInfrastructureOrEntityFramework()
    {
        var files = SourceFiles().ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(Lines)
            .Where(line => !IsComment(line.Text) && InfrastructureOrEntityFramework.IsMatch(line.Text))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text.Trim()}")
            .ToList();

        ArchitectureAssert.Passed(offenders, "No Features source file may name Infrastructure or EF Core");
    }

    [Fact]
    public void FeaturesTypes_ShouldNotDependOnThePersistenceNamespace()
    {
        var result = Types.InAssembly(FeaturesAssembly)
            .ShouldNot().HaveDependencyOn(PersistenceNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, $"No type in Features may depend on {PersistenceNamespace}");
    }

    // Named type by type, so the rule does not rest on the namespace the context and models live in today
    [Fact]
    public void FeaturesTypes_ShouldNotDependOnDContextOrAnyPersistenceModel()
    {
        var forbidden = PersistenceTypes().Select(type => type.FullName!).ToArray();
        Assert.Contains(typeof(DContext).FullName!, forbidden);
        Assert.True(forbidden.Length > 1, "No persistence model (Db*) was found in Infrastructure");

        var result = Types.InAssembly(FeaturesAssembly)
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        ArchitectureAssert.Passed(result, "No type in Features may reference DContext or a Db* type");
    }

    // Reflection over every member signature, independent of NetArchTest
    [Fact]
    public void FeaturesMemberSignatures_DoNotUseDContextOrAPersistenceModel()
    {
        var forbidden = PersistenceTypes().ToHashSet();

        var offenders = FeaturesAssembly.GetTypes()
            .SelectMany(type => SignatureTypes(type).Where(used => Mentions(used, forbidden)).Select(used => $"{type.FullName}: {used}"))
            .Distinct()
            .ToList();

        ArchitectureAssert.Passed(offenders, "No Features type may use DContext or a Db* type in a signature");
    }

    // Reads the source too: a constant is inlined and an unused using directive leaves no trace in the assembly
    [Fact]
    public void FeaturesSourceFiles_DoNotNameThePersistenceNamespace()
    {
        var files = SourceFiles().ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(Lines)
            .Where(line => line.Text.Contains(PersistenceNamespace, StringComparison.Ordinal))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text.Trim()}")
            .ToList();

        ArchitectureAssert.Passed(offenders, $"No Features source file may name {PersistenceNamespace}");
    }

    [Fact]
    public void FeaturesSourceFiles_DoNotNameDContextOrAPersistenceModel()
    {
        var offenders = SourceFiles()
            .SelectMany(Lines)
            .Where(line => !IsComment(line.Text) && ContextOrPersistenceModel.IsMatch(line.Text))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text.Trim()}")
            .ToList();

        ArchitectureAssert.Passed(offenders, "No Features source file may name DContext or a Db* type");
    }

    // DContext and every persistence model: the Infrastructure types named Db* in the persistence namespace
    private static IEnumerable<Type> PersistenceTypes() =>
        InfrastructureAssembly.GetTypes()
            .Where(type => type == typeof(DContext)
                || (type.Name.StartsWith(PersistenceModelPrefix, StringComparison.Ordinal)
                    && type.Namespace?.StartsWith(PersistenceNamespace, StringComparison.Ordinal) == true
                    && !type.IsNested));

    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var fromBase = type.BaseType is null ? [] : new[] { type.BaseType };
        var fields = type.GetFields(all).Select(field => field.FieldType);
        var properties = type.GetProperties(all).Select(property => property.PropertyType);
        var methods = type.GetMethods(all)
            .SelectMany(method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType));
        var constructors = type.GetConstructors(all).SelectMany(ctor => ctor.GetParameters().Select(p => p.ParameterType));

        return fromBase.Concat(type.GetInterfaces()).Concat(fields).Concat(properties).Concat(methods).Concat(constructors);
    }

    // The type itself, its element type or any of its generic arguments
    private static bool Mentions(Type used, HashSet<Type> forbidden)
    {
        if (forbidden.Contains(used)) return true;
        if (used.HasElementType && Mentions(used.GetElementType()!, forbidden)) return true;
        return used.IsGenericType && used.GetGenericArguments().Any(argument => Mentions(argument, forbidden));
    }

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    private static IEnumerable<(string Path, int Line, string Text)> Lines((string Path, string Text) file) =>
        file.Text.Split('\n').Select((line, index) => (file.Path, index + 1, line));

    private static IEnumerable<(string Path, string Text)> SourceFiles()
    {
        var root = Path.Combine(SolutionFiles.RepositoryRoot, FeaturesProjectFolder);
        return Directory.EnumerateFiles(root, SourcePattern, SearchOption.AllDirectories)
            .Select(path => (Relative: Path.GetRelativePath(root, path).Replace('\\', '/'), Full: path))
            .Where(file => !GeneratedFolders.Contains(file.Relative.Split('/')[0]))
            .Select(file => (file.Relative, File.ReadAllText(file.Full)));
    }
}

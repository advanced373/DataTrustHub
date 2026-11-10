using System.Text.RegularExpressions;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Clearances;
using DataTrustHub.Domain.ReadModels.Policies;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.GetClearance;
using DataTrustHub.Features.Administration.GetPolicy;
using DataTrustHub.Features.Administration.ListClearances;
using DataTrustHub.Features.Administration.ListPolicies;
using DataTrustHub.Features.Tests.Build.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// #74 (ADR-0012 step 4, part 4c): the policy and clearance queries of Administration read through the read-side
// ports. AC1: the four slices (get policy, list policies, get clearance, list clearances) do not see DContext, a
// persistence model (Db*), Infrastructure or EF Core. AC2: the new ports and read models live in Domain/ReadModels
// free of EF Core and Infrastructure, their Infrastructure adapters are registered, and every handler of the four
// slices resolves from the API container
public class PolicyAndClearanceReadSideArchitectureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AdministrationFolder = "Administration";
    private const string AdministrationNamespace = FeaturesRoot + NamespaceSeparator + AdministrationFolder;
    private const string FeaturesProjectFolder = "DataTrustHub.Features";
    private const string SourcePattern = "*.cs";

    private static readonly string[] SliceFolders =
        ["GetPolicy", "ListPolicies", "GetClearance", "ListClearances"];

    // What a source file of the four slices must not name: the context, a persistence model, the Infrastructure
    // namespace or EF Core
    private static readonly Regex PersistenceReference =
        new(@"\bDContext\b|\bDb[A-Z]\w*|DataTrustHub\.Infrastructure|Microsoft\.EntityFrameworkCore", RegexOptions.CultureInvariant);

    private static readonly Type[] Handlers =
        [typeof(GetPolicyHandler), typeof(ListPoliciesHandler), typeof(GetClearanceHandler), typeof(ListClearancesHandler)];

    private static readonly Type[] Ports = [typeof(IPolicyQueries), typeof(IClearanceQueries)];

    private static readonly Type[] ReadModels =
        [typeof(PolicyEntry), typeof(ClearanceEntry)];

    // The plain values a read model may hold (nullable forms included)
    private static readonly Type[] PrimitiveValues =
        [typeof(Guid), typeof(string), typeof(int), typeof(long), typeof(bool)];

    public static TheoryData<Type> HandlerTypes() => new(Handlers);

    public static TheoryData<string> SliceFolderNames() => new(SliceFolders);

    public static TheoryData<Type> PortTypes() => new(Ports);

    public static TheoryData<Type> ReadModelTypes() => new(ReadModels);

    // --- AC1: the four handlers and their slices ---

    [Theory]
    [MemberData(nameof(HandlerTypes))]
    public void Handler_ShouldNotDependOnInfrastructureOrEntityFramework(Type handler)
    {
        var result = Types.InAssembly(FeaturesAssembly)
            .That().HaveName(handler.Name)
            .ShouldNot().HaveDependencyOnAny(InfrastructureNamespace, EntityFrameworkNamespace)
            .GetResult();

        Assert.Single(Types.InAssembly(FeaturesAssembly).That().HaveName(handler.Name).GetTypes());
        ArchitectureAssert.Passed(result, $"{handler.Name} must not depend on DContext, Db* types, Infrastructure or EF Core");
    }

    [Theory]
    [MemberData(nameof(SliceFolderNames))]
    public void SliceTypes_ShouldNotDependOnInfrastructureOrEntityFramework(string sliceFolder)
    {
        var selection = Types.InAssembly(FeaturesAssembly)
            .That().ResideInNamespaceMatching(NamespaceOrChildren(SliceNamespace(sliceFolder)));
        ArchitectureAssert.NotEmpty(selection, $"the {sliceFolder} types");

        var result = selection
            .ShouldNot().HaveDependencyOnAny(InfrastructureNamespace, EntityFrameworkNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, $"{sliceFolder} must not depend on DContext, Db* types, Infrastructure or EF Core");
    }

    // Reads the source too, so an unused using directive, which leaves no trace in the assembly, is caught
    [Theory]
    [MemberData(nameof(SliceFolderNames))]
    public void SliceSourceFiles_DoNotNamePersistenceTypesInfrastructureOrEntityFramework(string sliceFolder)
    {
        var files = SourceFiles(sliceFolder).ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(file => file.Text.Split('\n').Select((line, index) => (file.Path, Line: index + 1, Text: line)))
            .Where(line => !IsComment(line.Text) && PersistenceReference.IsMatch(line.Text))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text.Trim()}")
            .ToList();

        ArchitectureAssert.Passed(offenders, $"{sliceFolder} source must not name DContext, Db*, Infrastructure or EF Core");
    }

    // The authorization check stays on the membership repository (AC4); the policies come from the read side
    [Fact]
    public void GetPolicyHandler_DependsOnlyOnTheMembershipRepositoryAndThePolicyQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IMembershipRepository), typeof(IPolicyQueries) },
            ConstructorDependencies(typeof(GetPolicyHandler)));
    }

    [Fact]
    public void ListPoliciesHandler_DependsOnlyOnTheMembershipRepositoryAndThePolicyQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IMembershipRepository), typeof(IPolicyQueries) },
            ConstructorDependencies(typeof(ListPoliciesHandler)));
    }

    // The authorization check stays on the membership repository (AC4); the clearances come from the read side
    [Fact]
    public void GetClearanceHandler_DependsOnlyOnTheMembershipRepositoryAndTheClearanceQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IMembershipRepository), typeof(IClearanceQueries) },
            ConstructorDependencies(typeof(GetClearanceHandler)));
    }

    [Fact]
    public void ListClearancesHandler_DependsOnlyOnTheMembershipRepositoryAndTheClearanceQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IMembershipRepository), typeof(IClearanceQueries) },
            ConstructorDependencies(typeof(ListClearancesHandler)));
    }

    // --- AC2: ports and read models in Domain/ReadModels, adapters in Infrastructure ---

    [Theory]
    [MemberData(nameof(PortTypes))]
    public void Port_IsAnInterfaceInDomainReadModels(Type port)
    {
        Assert.True(port.IsInterface);
        Assert.Same(DomainAssembly, port.Assembly);
        Assert.Matches(NamespaceOrChildren(ReadModelsNamespace), port.Namespace);
    }

    [Theory]
    [MemberData(nameof(PortTypes))]
    public void Port_ShouldNotDependOnEntityFrameworkOrInfrastructure(Type port)
    {
        AssertNoPersistenceDependency(port);
    }

    [Theory]
    [MemberData(nameof(ReadModelTypes))]
    public void ReadModel_IsASealedRecordInDomainReadModels(Type readModel)
    {
        Assert.Same(DomainAssembly, readModel.Assembly);
        Assert.Matches(NamespaceOrChildren(ReadModelsNamespace), readModel.Namespace);
        Assert.NotNull(readModel.GetMethod("<Clone>$"));
        Assert.True(readModel.IsSealed);
    }

    [Theory]
    [MemberData(nameof(ReadModelTypes))]
    public void ReadModel_ShouldNotDependOnEntityFrameworkOrInfrastructure(Type readModel)
    {
        AssertNoPersistenceDependency(readModel);
    }

    [Theory]
    [MemberData(nameof(ReadModelTypes))]
    public void ReadModel_HoldsOnlyPrimitiveValues(Type readModel)
    {
        var nonPrimitive = readModel.GetProperties()
            .Where(property => !PrimitiveValues.Contains(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType))
            .Select(property => $"{readModel.Name}.{property.Name}: {property.PropertyType.Name}")
            .ToList();

        ArchitectureAssert.Passed(nonPrimitive, "A read model holds only primitive values");
    }

    // The read models are separate from the HTTP response records, which stay in the slices
    [Theory]
    [MemberData(nameof(ReadModelTypes))]
    public void ReadModel_IsNotAFeaturesType(Type readModel)
    {
        Assert.NotSame(FeaturesAssembly, readModel.Assembly);
        Assert.DoesNotMatch(NamespaceOrChildren(AdministrationNamespace), readModel.Namespace!);
    }

    [Theory]
    [MemberData(nameof(PortTypes))]
    public void Port_HasAnInternalSealedImplementationInInfrastructure(Type port)
    {
        var adapter = Assert.Single(
            InfrastructureAssembly.GetTypes(),
            type => type is { IsClass: true, IsAbstract: false } && port.IsAssignableFrom(type));

        Assert.True(adapter.IsSealed);
        Assert.False(adapter.IsPublic);
    }

    [Theory]
    [MemberData(nameof(PortTypes))]
    public void Port_ResolvesFromTheApiContainerToItsInfrastructureAdapter(Type port)
    {
        using var scope = factory.Services.CreateScope();

        var adapter = scope.ServiceProvider.GetRequiredService(port);

        Assert.Same(InfrastructureAssembly, adapter.GetType().Assembly);
    }

    // Scoped like the context it reads: one adapter per request scope, a different one in another scope
    [Theory]
    [MemberData(nameof(PortTypes))]
    public void Port_IsRegisteredAsScoped(Type port)
    {
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();

        Assert.Same(first.ServiceProvider.GetRequiredService(port), first.ServiceProvider.GetRequiredService(port));
        Assert.NotSame(first.ServiceProvider.GetRequiredService(port), second.ServiceProvider.GetRequiredService(port));
    }

    // Endpoints inject the handler interface they call, so each one resolving means each endpoint resolves its handler
    [Fact]
    public void PolicyAndClearanceQueryHandlers_ResolveFromTheApiContainer()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.NotNull(services.GetRequiredService<IQueryHandler<GetPolicyQuery, Result<GetPolicyResponse>>>());
        Assert.NotNull(services.GetRequiredService<IQueryHandler<ListPoliciesQuery, Result<IReadOnlyList<PolicySummary>>>>());
        Assert.NotNull(services.GetRequiredService<IQueryHandler<GetClearanceQuery, Result<GetClearanceResponse>>>());
        Assert.NotNull(services.GetRequiredService<IQueryHandler<ListClearancesQuery, Result<IReadOnlyList<ClearanceSummary>>>>());
    }

    private static void AssertNoPersistenceDependency(Type type)
    {
        var result = Types.InAssembly(type.Assembly)
            .That().HaveName(type.Name).And().ResideInNamespace(type.Namespace)
            .ShouldNot().HaveDependencyOnAny(EntityFrameworkNamespace, InfrastructureNamespace, FeaturesRoot)
            .GetResult();

        ArchitectureAssert.Passed(result, $"{type.Name} must not depend on EF Core, Infrastructure or Features");
    }

    private static string SliceNamespace(string sliceFolder) =>
        AdministrationNamespace + NamespaceSeparator + sliceFolder;

    private static HashSet<Type> ConstructorDependencies(Type handler) =>
        handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToHashSet();

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    private static IEnumerable<(string Path, string Text)> SourceFiles(string sliceFolder)
    {
        var root = Path.Combine(SolutionFiles.RepositoryRoot, FeaturesProjectFolder, AdministrationFolder, sliceFolder);
        return Directory.EnumerateFiles(root, SourcePattern, SearchOption.AllDirectories)
            .Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)));
    }
}

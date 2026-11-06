using System.Text.RegularExpressions;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Domain.ReadModels.ClassificationOptions;
using DataTrustHub.Domain.ReadModels.DataItems;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.DataManagement.GetDataItem;
using DataTrustHub.Features.DataManagement.ListClassificationOptions;
using DataTrustHub.Features.DataManagement.ListDataItems;
using DataTrustHub.Features.Tests.Build.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// #74 (ADR-0012 step 4, part 4a): the DataManagement query side reads through the read-side ports. AC1: nothing in
// the DataManagement folder sees DContext, a persistence model (Db*), Infrastructure or EF Core. AC2: the ports and
// read models live in Domain/ReadModels free of EF Core and Infrastructure, and their Infrastructure adapters are
// registered, so every handler resolves
public class DataManagementReadSideArchitectureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string DataManagementFolder = "DataManagement";
    private const string DataManagementNamespace = FeaturesRoot + NamespaceSeparator + DataManagementFolder;
    private const string FeaturesProjectFolder = "DataTrustHub.Features";
    private const string OldReadRuleFile = "DataAccess.cs";
    private const string OldReadRuleType = DataManagementNamespace + NamespaceSeparator + "DataAccess";
    private const string ReadRuleTypeName = "DataAccess";
    private const string SourcePattern = "*.cs";

    // What a DataManagement source file must not name: the context, a persistence model, the Infrastructure
    // namespace or EF Core
    private static readonly Regex PersistenceReference =
        new(@"\bDContext\b|\bDb[A-Z]\w*|DataTrustHub\.Infrastructure|Microsoft\.EntityFrameworkCore", RegexOptions.CultureInvariant);

    private static readonly Type[] Ports = [typeof(IDataItemQueries), typeof(IClassificationOptionQueries)];

    private static readonly Type[] ReadModels =
        [typeof(DataItemListEntry), typeof(ReadableDataItem), typeof(DeniedReadTarget), typeof(ClearedPolicy)];

    // The plain values a read model may hold (nullable forms included)
    private static readonly Type[] PrimitiveValues =
        [typeof(Guid), typeof(string), typeof(int), typeof(long), typeof(bool)];

    public static TheoryData<Type> PortTypes() => new(Ports);

    public static TheoryData<Type> ReadModelTypes() => new(ReadModels);

    // --- AC1: the DataManagement folder ---

    [Fact]
    public void DataManagementTypes_ShouldNotDependOnInfrastructureOrEntityFramework()
    {
        var selection = Types.InAssembly(FeaturesAssembly)
            .That().ResideInNamespaceMatching(NamespaceOrChildren(DataManagementNamespace));
        ArchitectureAssert.NotEmpty(selection, "the DataManagement types");

        var result = selection
            .ShouldNot().HaveDependencyOnAny(InfrastructureNamespace, EntityFrameworkNamespace)
            .GetResult();

        ArchitectureAssert.Passed(result, "DataManagement must not depend on DContext, Db* types, Infrastructure or EF Core");
    }

    // Reads the source too, so an unused using directive, which leaves no trace in the assembly, is caught
    [Fact]
    public void DataManagementSourceFiles_DoNotNamePersistenceTypesInfrastructureOrEntityFramework()
    {
        var files = DataManagementSourceFiles().ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(file => file.Text.Split('\n').Select((line, index) => (file.Path, Line: index + 1, Text: line)))
            .Where(line => !IsComment(line.Text) && PersistenceReference.IsMatch(line.Text))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text.Trim()}")
            .ToList();

        ArchitectureAssert.Passed(offenders, "DataManagement source must not name DContext, Db*, Infrastructure or EF Core");
    }

    [Fact]
    public void ReadRule_IsNoLongerPartOfFeatures()
    {
        Assert.Null(FeaturesAssembly.GetType(OldReadRuleType));
        Assert.DoesNotContain(FeaturesAssembly.GetTypes(), type => type.Name == ReadRuleTypeName);
        Assert.False(File.Exists(Path.Combine(DataManagementRoot(), OldReadRuleFile)));
    }

    [Fact]
    public void ReadRule_LivesInInfrastructure()
    {
        Assert.Contains(InfrastructureAssembly.GetTypes(), type => type.Name == ReadRuleTypeName && type.IsAbstract && type.IsSealed);
    }

    [Fact]
    public void ListDataItemsHandler_DependsOnlyOnTheDataItemQueries()
    {
        Assert.Equal(new HashSet<Type> { typeof(IDataItemQueries) }, ConstructorDependencies(typeof(ListDataItemsHandler)));
    }

    [Fact]
    public void GetDataItemHandler_DependsOnlyOnTheDataItemQueriesTheAuditLogAndALogger()
    {
        Type[] expected = [typeof(IDataItemQueries), typeof(IAuditLog), typeof(ILogger<GetDataItemHandler>)];

        Assert.Equal(expected.ToHashSet(), ConstructorDependencies(typeof(GetDataItemHandler)));
    }

    [Fact]
    public void ListClassificationOptionsHandler_DependsOnlyOnTheClassificationOptionQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IClassificationOptionQueries) },
            ConstructorDependencies(typeof(ListClassificationOptionsHandler)));
    }

    // --- AC2: ports and read models in Domain/ReadModels ---

    [Fact]
    public void ReadModelsTypes_ShouldNotDependOnEntityFrameworkOrInfrastructure()
    {
        var selection = Types.InAssembly(DomainAssembly)
            .That().ResideInNamespaceMatching(NamespaceOrChildren(ReadModelsNamespace));
        ArchitectureAssert.NotEmpty(selection, "the Domain/ReadModels types");

        var result = selection
            .ShouldNot().HaveDependencyOnAny(EntityFrameworkNamespace, InfrastructureNamespace, FeaturesRoot)
            .GetResult();

        ArchitectureAssert.Passed(result, "Domain/ReadModels must not depend on EF Core, Infrastructure or Features");
    }

    [Theory]
    [MemberData(nameof(PortTypes))]
    public void Port_IsAnInterfaceInDomainReadModels(Type port)
    {
        Assert.True(port.IsInterface);
        Assert.Same(DomainAssembly, port.Assembly);
        Assert.Matches(NamespaceOrChildren(ReadModelsNamespace), port.Namespace);
    }

    [Theory]
    [MemberData(nameof(ReadModelTypes))]
    public void ReadModel_IsARecordInDomainReadModels(Type readModel)
    {
        Assert.Same(DomainAssembly, readModel.Assembly);
        Assert.Matches(NamespaceOrChildren(ReadModelsNamespace), readModel.Namespace);
        Assert.NotNull(readModel.GetMethod("<Clone>$"));
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

    [Theory]
    [MemberData(nameof(PortTypes))]
    public void Port_HasAnImplementationInInfrastructure(Type port)
    {
        Assert.Contains(InfrastructureAssembly.GetTypes(), type => type is { IsClass: true, IsAbstract: false } && port.IsAssignableFrom(type));
    }

    [Theory]
    [MemberData(nameof(PortTypes))]
    public void Port_ResolvesFromTheApiContainerToItsInfrastructureAdapter(Type port)
    {
        using var scope = factory.Services.CreateScope();

        var adapter = scope.ServiceProvider.GetRequiredService(port);

        Assert.Same(InfrastructureAssembly, adapter.GetType().Assembly);
    }

    // Endpoints inject the handler interface they call, so each one resolving means each endpoint resolves its handler
    [Fact]
    public void EveryHandlerInterface_ResolvesFromTheApiContainer()
    {
        var handlerInterfaces = HandlerInterfaces().ToList();
        Assert.NotEmpty(handlerInterfaces);
        using var scope = factory.Services.CreateScope();

        var unresolved = handlerInterfaces
            .Where(handler => scope.ServiceProvider.GetService(handler) is null)
            .Select(handler => handler.ToString())
            .ToList();

        ArchitectureAssert.Passed(unresolved, "Every command and query handler must resolve from the API container");
    }

    [Fact]
    public void DataManagementQueryHandlers_ResolveFromTheApiContainer()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.NotNull(services.GetRequiredService<IQueryHandler<ListDataItemsQuery, Result<IReadOnlyList<DataItemSummary>>>>());
        Assert.NotNull(services.GetRequiredService<IQueryHandler<GetDataItemQuery, Result<GetDataItemResponse>>>());
        Assert.NotNull(services.GetRequiredService<IQueryHandler<ListClassificationOptionsQuery, Result<ClassificationOptionsResponse>>>());
    }

    private static IEnumerable<Type> HandlerInterfaces() =>
        FeaturesAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(i => i.IsGenericType
                && (i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>) || i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
                && !i.ContainsGenericParameters)
            .Distinct();

    private static HashSet<Type> ConstructorDependencies(Type handler) =>
        handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToHashSet();

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    private static string DataManagementRoot() =>
        Path.Combine(SolutionFiles.RepositoryRoot, FeaturesProjectFolder, DataManagementFolder);

    private static IEnumerable<(string Path, string Text)> DataManagementSourceFiles()
    {
        var root = DataManagementRoot();
        return Directory.EnumerateFiles(root, SourcePattern, SearchOption.AllDirectories)
            .Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)));
    }
}

using System.Reflection;
using System.Text.RegularExpressions;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.ListAuditRecords;
using DataTrustHub.Features.Administration.ListOrganizationAuditRecords;
using DataTrustHub.Features.Tests.Build.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;
using OrganizationSummary = DataTrustHub.Features.Administration.ListOrganizationAuditRecords.AuditRecordSummary;
using PlatformSummary = DataTrustHub.Features.Administration.ListAuditRecords.AuditRecordSummary;

namespace DataTrustHub.Features.Tests.Architecture;

// #74 (ADR-0012 step 4, part 4e): the two audit trail queries of Administration read through the read side. AC1: the
// two slices (the platform trail and the organization trail) do not see DContext, a persistence model (Db*),
// Infrastructure or EF Core. AC2: the port and read models live in Domain/ReadModels free of EF Core and
// Infrastructure, the Infrastructure adapter is registered, and both handlers resolve from the API container. AC4:
// the read model carries nothing about a user but the actor's id and email, and the read port has no write method
// (A3). AC6: the action filter's limit comes from the Domain, with its value unchanged
public class AuditTrailReadSideArchitectureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AdministrationFolder = "Administration";
    private const string AdministrationNamespace = FeaturesRoot + NamespaceSeparator + AdministrationFolder;
    private const string FeaturesProjectFolder = "DataTrustHub.Features";
    private const string SourcePattern = "*.cs";

    // The action column's length before this part (DbAuditRecord.ActionMaxLength = 128); it must not change
    private const int ActionColumnLength = 128;

    // A property whose name says it holds something about a user
    private static readonly Regex UserDataName =
        new("user|actor|email|password|hash|role|name", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // A method name that says it changes the trail (A3: the trail is append-only, and the read side only reads)
    private static readonly Regex WriteMethodName =
        new("^(Add|Append|Insert|Create|Update|Set|Change|Delete|Remove|Purge|Clear|Save|Write)",
            RegexOptions.CultureInvariant);

    private static readonly string[] SliceFolders = ["ListAuditRecords", "ListOrganizationAuditRecords"];

    // What a source file of the two slices must not name: the context, a persistence model, the Infrastructure
    // namespace or EF Core
    private static readonly Regex PersistenceReference =
        new(@"\bDContext\b|\bDb[A-Z]\w*|DataTrustHub\.Infrastructure|Microsoft\.EntityFrameworkCore", RegexOptions.CultureInvariant);

    private static readonly Type[] Handlers = [typeof(ListAuditRecordsHandler), typeof(ListOrganizationAuditRecordsHandler)];

    private static readonly Type[] Ports = [typeof(IAuditRecordQueries)];

    private static readonly Type[] ReadModels = [typeof(AuditRecordEntry), typeof(AuditRecordFilter)];

    // The plain values a read model may hold (nullable forms included)
    private static readonly Type[] PrimitiveValues =
        [typeof(Guid), typeof(string), typeof(int), typeof(long), typeof(bool), typeof(DateTime)];

    public static TheoryData<Type> HandlerTypes() => new(Handlers);

    public static TheoryData<string> SliceFolderNames() => new(SliceFolders);

    public static TheoryData<Type> PortTypes() => new(Ports);

    public static TheoryData<Type> ReadModelTypes() => new(ReadModels);

    // --- AC1: the two handlers and their slices ---

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

    // Reads the source too, so an unused using directive or a constant (inlined, so no trace in the assembly) is caught
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

    [Fact]
    public void ListAuditRecordsHandler_DependsOnlyOnTheAuditRecordQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IAuditRecordQueries) },
            ConstructorDependencies(typeof(ListAuditRecordsHandler)));
    }

    // The organization admin check stays on the membership repository (AC4); the records come from the read side
    [Fact]
    public void ListOrganizationAuditRecordsHandler_DependsOnTheMembershipRepositoryAndTheAuditRecordQueries()
    {
        Assert.Equal(
            new HashSet<Type> { typeof(IMembershipRepository), typeof(IAuditRecordQueries) },
            ConstructorDependencies(typeof(ListOrganizationAuditRecordsHandler)));
    }

    // --- AC2: port and read models in Domain/ReadModels, adapter in Infrastructure ---

    [Theory]
    [MemberData(nameof(PortTypes))]
    public void Port_IsAnInterfaceInDomainReadModels(Type port)
    {
        Assert.True(port.IsInterface);
        Assert.Same(DomainAssembly, port.Assembly);
        Assert.Matches(NamespaceOrChildren(ReadModelsNamespace), port.Namespace);
    }

    // Separate from the append-only write port IAuditLog, which stays in Domain/Auditing
    [Fact]
    public void AuditRecordQueries_IsNotTheWritePort()
    {
        Assert.NotEqual(typeof(IAuditLog).Namespace, typeof(IAuditRecordQueries).Namespace);
        Assert.False(typeof(IAuditLog).IsAssignableFrom(typeof(IAuditRecordQueries)));
        Assert.Empty(typeof(IAuditRecordQueries).GetInterfaces());
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
    public void AuditTrailQueryHandlers_ResolveFromTheApiContainer()
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.NotNull(services.GetRequiredService<
            IQueryHandler<ListAuditRecordsQuery, Result<IReadOnlyList<PlatformSummary>>>>());
        Assert.NotNull(services.GetRequiredService<
            IQueryHandler<ListOrganizationAuditRecordsQuery, Result<IReadOnlyList<OrganizationSummary>>>>());
    }

    // --- AC4: nothing about a user but the actor's id and email; no write on the read port ---

    [Fact]
    public void AuditRecordEntry_CarriesNothingAboutAUserButTheActorsIdAndEmail()
    {
        var userData = typeof(AuditRecordEntry).GetProperties()
            .Where(property => UserDataName.IsMatch(property.Name))
            .Select(property => property.Name)
            .ToHashSet();

        Assert.Equal(
            new HashSet<string> { nameof(AuditRecordEntry.ActorUserId), nameof(AuditRecordEntry.ActorEmail) },
            userData);
    }

    // The filter names a user only by the actor's id
    [Fact]
    public void AuditRecordFilter_NamesAUserOnlyByTheActorsId()
    {
        var userData = typeof(AuditRecordFilter).GetProperties()
            .Where(property => UserDataName.IsMatch(property.Name))
            .Select(property => property.Name)
            .ToHashSet();

        Assert.Equal(new HashSet<string> { nameof(AuditRecordFilter.ActorUserId) }, userData);
    }

    [Fact]
    public void AuditRecordQueries_HasNoWriteMethod()
    {
        var writes = DeclaredMethods(typeof(IAuditRecordQueries))
            .Where(method => WriteMethodName.IsMatch(method.Name))
            .Select(method => method.Name)
            .ToList();

        Assert.NotEmpty(DeclaredMethods(typeof(IAuditRecordQueries)));
        ArchitectureAssert.Passed(writes, "The audit trail's read port must not update, delete or add a record (A3)");
    }

    // Every method answers with the records it read: a list of read models, never a bare Task
    [Fact]
    public void AuditRecordQueries_EveryMethodReturnsAListOfAuditRecordEntries()
    {
        var methods = DeclaredMethods(typeof(IAuditRecordQueries)).ToList();

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
            Assert.Equal(typeof(Task<IReadOnlyList<AuditRecordEntry>>), method.ReturnType));
        Assert.Empty(typeof(IAuditRecordQueries).GetProperties());
    }

    // --- AC6: the action filter's limit lives in the Domain ---

    [Fact]
    public void AuditRecordActionMaxLength_IsDeclaredInTheDomainWithTheColumnsLength()
    {
        var field = typeof(AuditRecord).GetField(nameof(AuditRecord.ActionMaxLength), BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.True(field.IsLiteral);
        Assert.Equal(ActionColumnLength, AuditRecord.ActionMaxLength);
    }

    [Fact]
    public void DbAuditRecordActionMaxLength_EqualsTheDomainLimit()
    {
        Assert.Equal(AuditRecord.ActionMaxLength, DbAuditRecord.ActionMaxLength);
    }

    private static IEnumerable<MethodInfo> DeclaredMethods(Type port) =>
        port.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName);

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

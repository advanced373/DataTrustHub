using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DataTrustHub.Domain.ReadModels.Auditing;
using DataTrustHub.Domain.ReadModels.ClassificationOptions;
using DataTrustHub.Domain.ReadModels.Clearances;
using DataTrustHub.Domain.ReadModels.DataItems;
using DataTrustHub.Domain.ReadModels.Memberships;
using DataTrustHub.Domain.ReadModels.Organizations;
using DataTrustHub.Domain.ReadModels.Policies;
using DataTrustHub.Domain.ReadModels.Registrations;
using DataTrustHub.Domain.ReadModels.Users;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// #74 (ADR-0012 step 5, AC3): the read side, project-wide. Every read-side port (I*Queries) lives in
// Domain/ReadModels, has exactly one internal sealed implementation in Infrastructure/Persistance/ReadModels, is
// registered as scoped and resolves from the API container; every read model is a sealed record of primitive values.
// The ports and read models are discovered, so a new one is checked without editing this file. Replaces the per-part
// copies of steps 4a-4e
public class ReadSideArchitectureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ReadSideAdapterNamespace = InfrastructureNamespace + NamespaceSeparator + "Persistance"
        + NamespaceSeparator + "ReadModels";

    private const string CloneMethod = "<Clone>$";

    // A read-side port: I<Something>Queries
    private static readonly Regex PortName = new(@"^I[A-Z]\w*Queries$", RegexOptions.CultureInvariant);

    // A property whose name says it holds a password or a hash of one
    private static readonly Regex PasswordOrHashName =
        new("password|hash", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // The plain values a read model may hold (nullable forms included)
    private static readonly Type[] PrimitiveValues =
        [typeof(Guid), typeof(string), typeof(int), typeof(long), typeof(bool), typeof(DateTime)];

    // Every read-side port known today. The discovery must find each one, so a port moved out of Domain/ReadModels
    // or renamed away from I*Queries fails a test instead of silently leaving the rules below
    private static readonly Type[] KnownPorts =
    [
        typeof(IAuditRecordQueries), typeof(IClassificationOptionQueries), typeof(IClearanceQueries),
        typeof(IDataItemQueries), typeof(IMembershipQueries), typeof(IOrganizationQueries), typeof(IPolicyQueries),
        typeof(IRegistrationRequestQueries), typeof(IUserQueries),
    ];

    // Every read model known today, for the same reason: a read model moved out of Domain/ReadModels fails a test
    private static readonly Type[] KnownReadModels =
    [
        typeof(AuditRecordEntry), typeof(AuditRecordFilter), typeof(ClearedPolicy), typeof(ClearanceEntry),
        typeof(DataItemListEntry), typeof(DeniedReadTarget), typeof(ReadableDataItem), typeof(MyOrganization),
        typeof(OrganizationMember), typeof(OrganizationEntry), typeof(PolicyEntry), typeof(PendingRegistrationRequest),
        typeof(AccountEntry), typeof(UserByEmail),
    ];

    private static readonly Assembly[] SolutionAssemblies =
        [SharedKernelAssembly, DomainAssembly, InfrastructureAssembly, FeaturesAssembly];

    public static TheoryData<Type> PortTypes() => new(Ports());

    public static TheoryData<Type> ReadModelTypes() => new(ReadModels());

    // --- Discovery guards: a rule over an empty selection proves nothing ---

    [Fact]
    public void PortDiscovery_FindsEveryKnownPort()
    {
        var missing = KnownPorts.Except(Ports()).Select(port => port.FullName!).ToList();

        ArchitectureAssert.Passed(missing, "Every known read-side port must be discovered in Domain/ReadModels");
    }

    [Fact]
    public void ReadModelDiscovery_FindsEveryKnownReadModel()
    {
        var missing = KnownReadModels.Except(ReadModels()).Select(readModel => readModel.FullName!).ToList();

        ArchitectureAssert.Passed(missing, "Every known read model must be discovered in Domain/ReadModels");
    }

    [Fact]
    public void ReadModelDiscovery_ExcludesThePorts()
    {
        var readModels = ReadModels().ToList();

        Assert.DoesNotContain(readModels, KnownPorts.Contains);
    }

    // --- Ports ---

    // Searched in every solution assembly, so a port declared outside Domain/ReadModels is caught too
    [Fact]
    public void EveryQueriesInterface_LivesInDomainReadModels()
    {
        var misplaced = SolutionAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsInterface && PortName.IsMatch(type.Name))
            .Where(type => type.Assembly != DomainAssembly || !InReadModels(type))
            .Select(type => type.FullName!)
            .ToList();

        ArchitectureAssert.Passed(misplaced, "Every I*Queries interface must live in Domain/ReadModels");
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
    [MemberData(nameof(PortTypes))]
    public void Port_HasExactlyOneInternalSealedImplementationInInfrastructureReadModels(Type port)
    {
        var adapter = Assert.Single(
            SolutionAssemblies.SelectMany(assembly => assembly.GetTypes()),
            type => type is { IsClass: true, IsAbstract: false } && port.IsAssignableFrom(type));

        Assert.Same(InfrastructureAssembly, adapter.Assembly);
        Assert.Equal(ReadSideAdapterNamespace, adapter.Namespace);
        Assert.True(adapter.IsSealed, $"{adapter.Name} must be sealed");
        Assert.True(adapter.IsNotPublic, $"{adapter.Name} must be internal");
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

    // --- Read models ---

    [Theory]
    [MemberData(nameof(ReadModelTypes))]
    public void ReadModel_IsASealedRecordInDomainReadModels(Type readModel)
    {
        Assert.Same(DomainAssembly, readModel.Assembly);
        Assert.Matches(NamespaceOrChildren(ReadModelsNamespace), readModel.Namespace);
        Assert.NotNull(readModel.GetMethod(CloneMethod));
        Assert.True(readModel.IsSealed, $"{readModel.Name} must be sealed");
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
        Assert.DoesNotMatch(NamespaceOrChildren(FeaturesRoot), readModel.Namespace!);
    }

    // The password hash never leaves the write side (#74 part 4d, AC4), for every read model
    [Theory]
    [MemberData(nameof(ReadModelTypes))]
    public void ReadModel_CarriesNoPasswordHash(Type readModel)
    {
        var offenders = readModel.GetProperties()
            .Where(property => PasswordOrHashName.IsMatch(property.Name))
            .Select(property => $"{readModel.Name}.{property.Name}")
            .ToList();

        ArchitectureAssert.Passed(offenders, "A read model must not carry a password hash");
    }

    // The ports and read models together: no EF Core, Infrastructure or Features
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

    // --- Handlers ---

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

    private static IEnumerable<Type> Ports() =>
        DomainAssembly.GetTypes().Where(type => type.IsInterface && PortName.IsMatch(type.Name) && InReadModels(type));

    // Every non-interface type the compiler did not generate in Domain/ReadModels
    private static IEnumerable<Type> ReadModels() =>
        DomainAssembly.GetTypes()
            .Where(type => !type.IsInterface && !type.IsNested && InReadModels(type))
            .Where(type => type.GetCustomAttribute<CompilerGeneratedAttribute>() is null);

    private static bool InReadModels(Type type) =>
        Regex.IsMatch(type.Namespace ?? string.Empty, NamespaceOrChildren(ReadModelsNamespace));

    private static IEnumerable<Type> HandlerInterfaces() =>
        FeaturesAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(i => i.IsGenericType
                && (i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>) || i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
                && !i.ContainsGenericParameters)
            .Distinct();
}

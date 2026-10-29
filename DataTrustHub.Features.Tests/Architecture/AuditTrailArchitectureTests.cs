using System.Reflection;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.DataManagement.AddDataItem;
using DataTrustHub.Features.DataManagement.DeleteDataItem;
using DataTrustHub.SharedKernel;
using Xunit;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// #24 step 1 (ADR-0016): the audit log port is append-only (AC2), every command can be audited (AC5), and
// only the two use cases of AC6 fill the audit scope
public class AuditTrailArchitectureTests
{
    private const string AppendAsyncName = "AppendAsync";
    private const string AuditingFolder = "Auditing";
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void AuditLog_HasOnlyAppendAsync()
    {
        var method = Assert.Single(typeof(IAuditLog).GetMethods(DeclaredMembers));

        Assert.Equal(AppendAsyncName, method.Name);
        Assert.Equal(typeof(Task), method.ReturnType);
        Assert.Equal([typeof(AuditRecord), typeof(CancellationToken)], method.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void AuditLog_HasNoPropertiesAndNoBaseInterfaces()
    {
        Assert.Empty(typeof(IAuditLog).GetProperties(DeclaredMembers));
        Assert.Empty(typeof(IAuditLog).GetInterfaces());
    }

    [Fact]
    public void AuditLog_IsADomainPort()
    {
        Assert.True(typeof(IAuditLog).IsInterface);
        Assert.Equal(DomainAssembly, typeof(IAuditLog).Assembly);
    }

    // The audit decorator requires Result or Result<T>, so it can wrap every command handler
    [Fact]
    public void EveryCommandHandler_ReturnsAResult()
    {
        var resultTypes = FeaturesAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
            .Select(i => i.GetGenericArguments()[1])
            .ToList();

        Assert.NotEmpty(resultTypes);
        Assert.All(resultTypes, t => Assert.True(typeof(Result).IsAssignableFrom(t), t.Name));
    }

    // AC6: "Do not add AuditScope anywhere else"
    [Fact]
    public void OnlyAddAndDeleteDataItemHandlers_DependOnAuditScope()
    {
        var dependents = FeaturesAssembly.GetTypes()
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(AuditScope))))
            .Where(t => !t.IsGenericType)
            .ToHashSet();

        Assert.Equal(new HashSet<Type> { typeof(AddDataItemHandler), typeof(DeleteDataItemHandler) }, dependents);
    }

    [Fact]
    public void AuditingContracts_LiveInSharedAuditing()
    {
        Type[] contracts = [typeof(AuditScope), typeof(AuditTarget), typeof(AuditTargetTypes), typeof(IOrganizationScopedCommand), typeof(ITargetedCommand)];
        var auditingNamespace = SharedNamespace + NamespaceSeparator + AuditingFolder;

        Assert.All(contracts, t => Assert.Equal(auditingNamespace, t.Namespace));
    }
}

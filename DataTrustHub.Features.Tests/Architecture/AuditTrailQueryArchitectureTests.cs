using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.ListAuditRecords;
using DataTrustHub.Features.Administration.ListOrganizationAuditRecords;
using DataTrustHub.Features.Tests.Helpers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AuditTrailSetup;
using static DataTrustHub.Features.Tests.Architecture.ArchitectureConstants;

namespace DataTrustHub.Features.Tests.Architecture;

// #24 step 3: the two audit trail use cases are queries (AC6), and no route updates or deletes an audit record (AC7, A2)
public class AuditTrailQueryArchitectureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AuditRecordsSegment = "audit-records";
    private const string GetMethod = "GET";
    private const string AdministrationFolder = "Administration";
    private const string CommandSuffix = "Command";

    // The tenant-level actions that name the organization they act on
    private static readonly string[] OrganizationScopedTenantLevelActions = [AppointOrganizationAdminAction, DeleteOrganizationAction];

    public static TheoryData<string> TenantLevelActionNames() => new(TenantLevelActions);

    public static TheoryData<Type> AuditTrailHandlers() =>
        new(typeof(ListAuditRecordsHandler), typeof(ListOrganizationAuditRecordsHandler));

    [Theory]
    [MemberData(nameof(AuditTrailHandlers))]
    public void AuditTrailHandler_IsAQueryHandlerAndNotACommandHandler(Type handler)
    {
        var interfaces = handler.GetInterfaces().Where(i => i.IsGenericType).Select(i => i.GetGenericTypeDefinition()).ToList();

        Assert.Contains(typeof(IQueryHandler<,>), interfaces);
        Assert.DoesNotContain(typeof(ICommandHandler<,>), interfaces);
    }

    [Theory]
    [MemberData(nameof(AuditTrailHandlers))]
    public void AuditTrailHandler_LivesInItsOwnAdministrationSlice(Type handler)
    {
        var slice = handler.Name[..^HandlerSuffix.Length];

        Assert.Equal(string.Join(NamespaceSeparator, FeaturesRoot, AdministrationFolder, slice), handler.Namespace);
    }

    // The action names are hand-written strings: each must still name a command, as the audit decorator records it
    [Theory]
    [MemberData(nameof(TenantLevelActionNames))]
    public void TenantLevelAction_NamesACommandWithAHandler(string action)
    {
        var command = CommandType(action);

        Assert.Contains(FeaturesAssembly.GetTypes(), t => t.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) && i.GetGenericArguments()[0] == command));
    }

    [Theory]
    [MemberData(nameof(TenantLevelActionNames))]
    public void TenantLevelAction_IsOrganizationScopedOnlyForTheAppointmentAndTheOrganizationsDeletion(string action)
    {
        var isOrganizationScoped = typeof(IOrganizationScopedCommand).IsAssignableFrom(CommandType(action));

        Assert.Equal(OrganizationScopedTenantLevelActions.Contains(action), isOrganizationScoped);
    }

    [Fact]
    public void AuditRecordRoutes_AreBothMapped()
    {
        var patterns = AuditRecordRoutes().Select(e => e.RoutePattern.RawText).ToHashSet();

        Assert.Equal(
            new HashSet<string?> { "/admin/audit-records", "/organizations/{organizationId:guid}/audit-records" },
            patterns);
    }

    [Fact]
    public void AuditRecordRoutes_AnswerOnlyGet()
    {
        var methods = AuditRecordRoutes()
            .SelectMany(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? [])
            .ToHashSet();

        Assert.Equal(new HashSet<string> { GetMethod }, methods);
    }

    private static Type CommandType(string action) =>
        Assert.Single(FeaturesAssembly.GetTypes(), t => t.Name == action + CommandSuffix);

    private List<RouteEndpoint> AuditRecordRoutes() =>
        factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains(AuditRecordsSegment, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
}

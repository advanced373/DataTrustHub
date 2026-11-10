using System.Reflection;
using System.Text.RegularExpressions;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Auditing;
using DataTrustHub.Features.Administration.ListAuditRecords;
using DataTrustHub.Features.Administration.ListOrganizationAuditRecords;
using DataTrustHub.Infrastructure.Persistance.Model;
using Xunit;

namespace DataTrustHub.Features.Tests.Architecture;

// #74 (ADR-0012 step 4, part 4e): the two audit trail queries of Administration read through the read side: the exact
// constructor dependencies of the two handlers. AC4: the read model carries nothing about a user but the actor's id
// and email, and the read port is separate from the write port and has no write method (A3). AC6: the action
// filter's limit comes from the Domain, with its value unchanged. The layering rule (FeaturesPersistenceIsolationTests)
// and the ports and read models (ReadSideArchitectureTests) are checked project-wide (#74 step 5)
public class AuditTrailReadSideArchitectureTests
{
    // The action column's length before this part (DbAuditRecord.ActionMaxLength = 128); it must not change
    private const int ActionColumnLength = 128;

    // A property whose name says it holds something about a user
    private static readonly Regex UserDataName =
        new("user|actor|email|password|hash|role|name", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // A method name that says it changes the trail (A3: the trail is append-only, and the read side only reads)
    private static readonly Regex WriteMethodName =
        new("^(Add|Append|Insert|Create|Update|Set|Change|Delete|Remove|Purge|Clear|Save|Write)",
            RegexOptions.CultureInvariant);

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

    // Separate from the append-only write port IAuditLog, which stays in Domain/Auditing
    [Fact]
    public void AuditRecordQueries_IsNotTheWritePort()
    {
        Assert.NotEqual(typeof(IAuditLog).Namespace, typeof(IAuditRecordQueries).Namespace);
        Assert.False(typeof(IAuditLog).IsAssignableFrom(typeof(IAuditRecordQueries)));
        Assert.Empty(typeof(IAuditRecordQueries).GetInterfaces());
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

    private static HashSet<Type> ConstructorDependencies(Type handler) =>
        handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToHashSet();
}

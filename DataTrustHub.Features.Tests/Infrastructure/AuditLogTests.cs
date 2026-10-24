using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Auditing;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// AC3 of #24 step 1 (ADR-0016 points 4, 5): the audit log adapter on relational SQLite. It saves through its
// own short-lived context, so a record survives a request whose unit of work failed or was never saved,
// and it never submits the request's pending changes
public class AuditLogTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    private const string TableName = "AuditRecords";
    private const string Action = "AuditLogProbe";
    private const string TargetType = "DataItem";
    private const string ErrorCode = "Probe.Rejected";
    private const string RejectedName = "Rejected";
    private const int ActionMaxLength = 128;
    private const int TargetTypeMaxLength = 64;
    private const int OutcomeMaxLength = 16;
    private const int ErrorCodeMaxLength = 256;

    [Fact]
    public async Task AppendAsync_StoresEveryFieldWithTheOutcomeByName()
    {
        var record = AuditRecord.Create(
            DateTime.UtcNow, Guid.NewGuid(), Action, TargetType, Guid.NewGuid(), Guid.NewGuid(), AuditOutcome.Rejected, ErrorCode);

        await AppendInNewScopeAsync(record);

        var stored = await FindStoredAsync(record.Id);
        Assert.NotNull(stored);
        Assert.Equal(
            (record.ActorUserId, record.Action, record.TargetType, record.TargetId, record.OrganizationId, RejectedName, record.ErrorCode),
            (stored.ActorUserId, stored.Action, stored.TargetType, stored.TargetId, stored.OrganizationId, stored.Outcome, stored.ErrorCode));
        Assert.Equal(record.OccurredAt.Ticks, stored.OccurredAt.Ticks);
    }

    [Fact]
    public async Task AppendAsync_AfterTheRequestSaveFailed_StoresTheRecordAndDoesNotRetryTheRejectedChanges()
    {
        var userId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        await SaveMembershipsInNewScopeAsync(Membership.AddMember(userId, organizationId));
        var record = CreateFailedRecord();

        using (var scope = factory.Services.CreateScope())
        {
            var provider = scope.ServiceProvider;
            provider.GetRequiredService<IMembershipRepository>().Add(Membership.AddGuest(userId, organizationId));
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default));

            // Same request scope: the scoped context still holds the rejected membership
            await provider.GetRequiredService<IAuditLog>().AppendAsync(record, default);
        }

        Assert.NotNull(await FindStoredAsync(record.Id));
        Assert.Single(await StoredMembershipsAsync(userId));
    }

    [Fact]
    public async Task AppendAsync_WithPendingRequestChanges_DoesNotSaveThem()
    {
        var userId = Guid.NewGuid();
        var record = CreateFailedRecord();

        using (var scope = factory.Services.CreateScope())
        {
            var provider = scope.ServiceProvider;
            provider.GetRequiredService<IMembershipRepository>().Add(Membership.AddMember(userId, Guid.NewGuid()));

            // The request's unit of work is never saved: it is rolled back when the scope ends
            await provider.GetRequiredService<IAuditLog>().AppendAsync(record, default);
        }

        Assert.NotNull(await FindStoredAsync(record.Id));
        Assert.Empty(await StoredMembershipsAsync(userId));
    }

    [Fact]
    public async Task AppendAsync_DoesNotUseTheRequestContext()
    {
        var record = CreateFailedRecord();

        using var scope = factory.Services.CreateScope();
        var requestContext = scope.ServiceProvider.GetRequiredService<DContext>();
        await scope.ServiceProvider.GetRequiredService<IAuditLog>().AppendAsync(record, default);

        Assert.Empty(requestContext.ChangeTracker.Entries<DbAuditRecord>());
    }

    [Fact]
    public void Model_TableIsAuditRecords()
    {
        Assert.Equal(TableName, AuditRecordEntityType().GetTableName());
    }

    [Fact]
    public void Model_HasNoForeignKeysFromOrToAuditRecords()
    {
        var entityType = AuditRecordEntityType();

        Assert.Empty(entityType.GetForeignKeys());
        Assert.Empty(entityType.GetReferencingForeignKeys());
        Assert.Empty(entityType.GetNavigations());
    }

    [Fact]
    public void Model_HasIndexesOnOccurredAtAndOnOrganizationIdThenOccurredAt()
    {
        var indexes = AuditRecordEntityType().GetIndexes()
            .Select(i => string.Join(",", i.Properties.Select(p => p.Name)))
            .ToHashSet();

        Assert.Equal(
            new HashSet<string>
            {
                nameof(DbAuditRecord.OccurredAt),
                $"{nameof(DbAuditRecord.OrganizationId)},{nameof(DbAuditRecord.OccurredAt)}"
            },
            indexes);
    }

    [Theory]
    [InlineData(nameof(DbAuditRecord.Action), ActionMaxLength, false)]
    [InlineData(nameof(DbAuditRecord.TargetType), TargetTypeMaxLength, true)]
    [InlineData(nameof(DbAuditRecord.Outcome), OutcomeMaxLength, false)]
    [InlineData(nameof(DbAuditRecord.ErrorCode), ErrorCodeMaxLength, true)]
    public void Model_StringColumnsAreBounded(string column, int maxLength, bool nullable)
    {
        var property = AuditRecordEntityType().FindProperty(column)!;

        Assert.Equal((typeof(string), maxLength, nullable), (property.ClrType, property.GetMaxLength(), property.IsNullable));
    }

    [Fact]
    public void Model_OutcomeIsLongEnoughForEveryOutcomeName()
    {
        var maxLength = AuditRecordEntityType().FindProperty(nameof(DbAuditRecord.Outcome))!.GetMaxLength();

        Assert.All(Enum.GetNames<AuditOutcome>(), name => Assert.True(name.Length <= maxLength, name));
    }

    // --- helpers ---

    private static AuditRecord CreateFailedRecord() =>
        AuditRecord.Create(DateTime.UtcNow, null, Action, null, null, null, AuditOutcome.Failed, nameof(DbUpdateException));

    private IEntityType AuditRecordEntityType()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DContext>().Model.FindEntityType(typeof(DbAuditRecord))!;
    }

    private async Task AppendInNewScopeAsync(AuditRecord record)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditLog>().AppendAsync(record, default);
    }

    private async Task SaveMembershipsInNewScopeAsync(params Membership[] memberships)
    {
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IMembershipRepository>();
        foreach (var membership in memberships) repo.Add(membership);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
    }

    private async Task<DbAuditRecord?> FindStoredAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().AuditRecords
            .AsNoTracking().SingleOrDefaultAsync(r => r.Id == id);
    }

    private async Task<List<DbMembership>> StoredMembershipsAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().Memberships
            .AsNoTracking().Where(m => m.UserId == userId).ToListAsync();
    }
}

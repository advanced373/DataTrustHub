using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features._Shared.Auditing;
using DataTrustHub.Features.DataManagement.GetDataItem;
using DataTrustHub.Features.Tests.Auditing.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DataTrustHub.Features.Tests.Auditing;

// #24 step 2 on the GetDataItem handler alone, with a fake audit log: what each download and denied read records,
// and that a failed audit write propagates before any content is returned (fail closed), while on a denied read
// it is logged and the answer stays "not found"
public class GetDataItemAuditHandlerTests
{
    private const string DownloadAction = "DataItem.Download";
    private const string DataItemNotFoundCode = "DataItem.NotFound";
    private const string ItemName = "handler.txt";
    private const string Content = "aGFuZGxlci1jb250ZW50";
    private const string PersonalMarking = "UNCLASSIFIED";
    private const string ClassifiedMarking = "Confidential";
    private const string PolicyName = "Handler policy";
    private const string ClearanceName = "Handler clearance";
    private const string AuditStoreDown = "The audit store is down.";
    private const int Confidential = 2;
    private const long Size = 15;

    private readonly DContext db = new(new DbContextOptionsBuilder<DContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeAuditLog auditLog = new();
    private readonly CapturingLoggerProvider logs = new();
    private readonly Guid ownerId = Guid.NewGuid();
    private readonly Guid organizationId = Guid.NewGuid();

    // --- AC1 ---

    [Fact]
    public async Task Handle_OwnPersonalItem_AppendsSucceededWithoutOrganization()
    {
        var itemId = await SeedItemAsync(policyId: null, isDeleted: false);

        var result = await HandleAsync(itemId, ownerId);

        Assert.True(result.IsSuccess);
        AssertSingleRecord(ownerId, itemId, null, AuditOutcome.Succeeded, null);
    }

    [Fact]
    public async Task Handle_ClearedOnClassifiedItem_AppendsSucceededWithThePolicysOrganization()
    {
        var policyId = await SeedPolicyAsync();
        var itemId = await SeedItemAsync(policyId, isDeleted: false);
        var readerId = Guid.NewGuid();
        await SeedClearanceAsync(readerId, policyId, Confidential);

        var result = await HandleAsync(itemId, readerId);

        Assert.True(result.IsSuccess);
        AssertSingleRecord(readerId, itemId, organizationId, AuditOutcome.Succeeded, null);
    }

    // AC5: the time is taken when recording, in UTC
    [Fact]
    public async Task Handle_Download_RecordsTheUtcTimeOfTheAccess()
    {
        var itemId = await SeedItemAsync(policyId: null, isDeleted: false);
        var before = DateTime.UtcNow;

        await HandleAsync(itemId, ownerId);

        var record = Assert.Single(auditLog.Appended);
        Assert.Equal(DateTimeKind.Utc, record.OccurredAt.Kind);
        Assert.InRange(record.OccurredAt, before, DateTime.UtcNow);
    }

    // --- AC2 ---

    [Fact]
    public async Task Handle_ClassifiedWithoutClearance_AppendsDeniedWithThePolicysOrganization()
    {
        var policyId = await SeedPolicyAsync();
        var itemId = await SeedItemAsync(policyId, isDeleted: false);
        var intruderId = Guid.NewGuid();

        var result = await HandleAsync(itemId, intruderId);

        Assert.Equal(DataItemNotFoundCode, result.Error.Code);
        AssertSingleRecord(intruderId, itemId, organizationId, AuditOutcome.Denied, DataItemNotFoundCode);
    }

    [Fact]
    public async Task Handle_ClearanceBelowLevel_AppendsDenied()
    {
        var policyId = await SeedPolicyAsync();
        var itemId = await SeedItemAsync(policyId, isDeleted: false);
        var readerId = Guid.NewGuid();
        await SeedClearanceAsync(readerId, policyId, Confidential - 1);

        await HandleAsync(itemId, readerId);

        AssertSingleRecord(readerId, itemId, organizationId, AuditOutcome.Denied, DataItemNotFoundCode);
    }

    [Fact]
    public async Task Handle_PersonalItemOfAnotherUser_AppendsDeniedWithoutOrganization()
    {
        var itemId = await SeedItemAsync(policyId: null, isDeleted: false);
        var intruderId = Guid.NewGuid();

        await HandleAsync(itemId, intruderId);

        AssertSingleRecord(intruderId, itemId, null, AuditOutcome.Denied, DataItemNotFoundCode);
    }

    [Fact]
    public async Task Handle_SoftDeletedItem_AppendsDenied()
    {
        var itemId = await SeedItemAsync(policyId: null, isDeleted: true);

        await HandleAsync(itemId, ownerId);

        AssertSingleRecord(ownerId, itemId, null, AuditOutcome.Denied, DataItemNotFoundCode);
    }

    [Fact]
    public async Task Handle_ItemWhosePolicyNoLongerExists_AppendsDeniedWithoutOrganization()
    {
        var itemId = await SeedItemAsync(Guid.NewGuid(), isDeleted: false);

        await HandleAsync(itemId, ownerId);

        AssertSingleRecord(ownerId, itemId, null, AuditOutcome.Denied, DataItemNotFoundCode);
    }

    // --- AC3 ---

    [Fact]
    public async Task Handle_MissingItem_AppendsNothingAndReturnsNotFound()
    {
        var result = await HandleAsync(Guid.NewGuid(), ownerId);

        Assert.Equal(DataItemNotFoundCode, result.Error.Code);
        Assert.Empty(auditLog.Appended);
    }

    // --- AC4: fail closed ---

    [Fact]
    public async Task Handle_AuditWriteFailsOnDownload_PropagatesTheExceptionWithoutAResult()
    {
        var itemId = await SeedItemAsync(policyId: null, isDeleted: false);
        var failure = new InvalidOperationException(AuditStoreDown);
        auditLog.ThrowOnAppend = failure;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsync(itemId, ownerId));

        Assert.Same(failure, thrown);
    }

    // A denied read returns no content, so a failed audit write is logged and the answer stays 404 (ADR-0011)
    [Fact]
    public async Task Handle_AuditWriteFailsOnDeniedRead_StillReturnsNotFound()
    {
        var itemId = await SeedItemAsync(policyId: null, isDeleted: false);
        auditLog.ThrowOnAppend = new InvalidOperationException(AuditStoreDown);

        var result = await HandleAsync(itemId, Guid.NewGuid());

        Assert.Equal(DataItemNotFoundCode, result.Error.Code);
    }

    [Fact]
    public async Task Handle_AuditWriteFailsOnDeniedRead_LogsOneErrorNamingActionOutcomeTargetAndActor()
    {
        var itemId = await SeedItemAsync(policyId: null, isDeleted: false);
        var intruderId = Guid.NewGuid();
        auditLog.ThrowOnAppend = new InvalidOperationException(AuditStoreDown);

        await HandleAsync(itemId, intruderId);

        var entry = Assert.Single(logs.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.All(
            [DownloadAction, AuditOutcome.Denied.ToString(), itemId.ToString(), intruderId.ToString(), nameof(InvalidOperationException)],
            expected => Assert.Contains(expected, entry.Message));
    }

    // A2: the log holds neither the exception's message (it may quote the record) nor the file content
    [Fact]
    public async Task Handle_AuditWriteFailsOnDeniedRead_LogsNoExceptionMessageOrContent()
    {
        var itemId = await SeedItemAsync(policyId: null, isDeleted: false);
        auditLog.ThrowOnAppend = new InvalidOperationException(AuditStoreDown);

        await HandleAsync(itemId, Guid.NewGuid());

        var entry = Assert.Single(logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(AuditStoreDown, entry.Message);
        Assert.DoesNotContain(Content, entry.Message);
    }

    // A cancelled request is not swallowed on the denied path
    [Fact]
    public async Task Handle_AuditWriteFailsOnDeniedReadAfterCancellation_PropagatesTheException()
    {
        var itemId = await SeedItemAsync(policyId: null, isDeleted: false);
        using var cancellation = new CancellationTokenSource();
        var failure = new OperationCanceledException(cancellation.Token);
        var cancellingLog = new CancellingAuditLog(cancellation, failure);

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateHandler(cancellingLog).Handle(new GetDataItemQuery(itemId, Guid.NewGuid()), cancellation.Token));

        Assert.Same(failure, thrown);
        Assert.DoesNotContain(logs.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Handle_AuditWriteSucceeds_LogsNoError()
    {
        var itemId = await SeedItemAsync(policyId: null, isDeleted: false);

        await HandleAsync(itemId, Guid.NewGuid());

        Assert.DoesNotContain(logs.Entries, e => e.Level == LogLevel.Error);
    }

    // --- helpers ---

    private GetDataItemHandler CreateHandler(IAuditLog log) =>
        new(db, log, LoggerFactory.Create(builder => builder.AddProvider(logs)).CreateLogger<GetDataItemHandler>());

    private Task<DataTrustHub.SharedKernel.Result<GetDataItemResponse>> HandleAsync(Guid itemId, Guid requesterId) =>
        CreateHandler(auditLog).Handle(new GetDataItemQuery(itemId, requesterId), CancellationToken.None);

    private void AssertSingleRecord(Guid actorUserId, Guid itemId, Guid? expectedOrganizationId, AuditOutcome outcome, string? errorCode)
    {
        var record = Assert.Single(auditLog.Appended);
        Assert.Equal(
            (DownloadAction, (Guid?)actorUserId, (string?)AuditTargetTypes.DataItem, (Guid?)itemId),
            (record.Action, record.ActorUserId, record.TargetType, record.TargetId));
        Assert.Equal((expectedOrganizationId, outcome, errorCode), (record.OrganizationId, record.Outcome, record.ErrorCode));
    }

    private async Task<Guid> SeedPolicyAsync()
    {
        var policy = new DbPolicy { Id = Guid.NewGuid(), Name = PolicyName, OrganizationId = organizationId };
        db.Policies.Add(policy);
        await db.SaveChangesAsync();
        return policy.Id;
    }

    private async Task SeedClearanceAsync(Guid userId, Guid policyId, int priority)
    {
        db.Clearances.Add(new DbClearance
        {
            Id = Guid.NewGuid(),
            Name = ClearanceName,
            UserId = userId,
            PolicyId = policyId,
            ClassificationLevel = new DbClassificationLevel { Name = ClearanceName, Priority = priority }
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedItemAsync(Guid? policyId, bool isDeleted)
    {
        var item = new DbDataItem
        {
            Id = Guid.NewGuid(),
            Name = ItemName,
            Size = Size,
            Content = Content,
            OwnerUserId = ownerId,
            SecurityMarking = policyId is null ? PersonalMarking : ClassifiedMarking,
            PolicyId = policyId,
            ClassificationPriority = policyId is null ? null : Confidential,
            IsDeleted = isDeleted
        };
        db.DataItems.Add(item);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return item.Id;
    }

    // Cancels the request while the audit write is running, then fails like a cancelled database call
    private sealed class CancellingAuditLog(CancellationTokenSource cancellation, Exception failure) : IAuditLog
    {
        public Task AppendAsync(AuditRecord record, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            throw failure;
        }
    }
}

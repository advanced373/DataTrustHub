using DataTrustHub.Domain.ReadModels.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ListAuditRecords;

// From and To are an inclusive range on OccurredAt, in UTC
public record ListAuditRecordsQuery(Guid? UserId, string? Action, DateTime? From, DateTime? To);

// The platform admin reads the tenant-level audit trail (ADR-0016), newest first: the platform's own use
// cases only, never a tenant's trail. The trail is read only here: no use case updates or deletes an audit
// record (A3)
public class ListAuditRecordsHandler(IAuditRecordQueries auditRecordQueries)
    : IQueryHandler<ListAuditRecordsQuery, Result<IReadOnlyList<AuditRecordSummary>>>
{
    private readonly IAuditRecordQueries _auditRecordQueries = auditRecordQueries;

    public async Task<Result<IReadOnlyList<AuditRecordSummary>>> Handle(
        ListAuditRecordsQuery query,
        CancellationToken cancellationToken)
    {
        var records = await _auditRecordQueries.ListForActionsAsync(
            TenantLevelActions.All,
            ToFilter(query),
            Constants.MaxRecords,
            cancellationToken);

        return Result.Success<IReadOnlyList<AuditRecordSummary>>(records.Select(ToSummary).ToList());
    }

    private static AuditRecordFilter ToFilter(ListAuditRecordsQuery query) =>
        new(query.UserId, query.Action, ToUtc(query.From), ToUtc(query.To));

    private static AuditRecordSummary ToSummary(AuditRecordEntry record) =>
        new(
            record.Id,
            record.OccurredAt,
            record.ActorUserId,
            record.ActorEmail,
            record.Action,
            record.TargetType,
            record.TargetId,
            record.Outcome,
            record.ErrorCode);

    // A filter time without a kind is taken as UTC; a local one is converted
    private static DateTime? ToUtc(DateTime? time) =>
        time is null ? null
        : time.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time.Value, DateTimeKind.Utc)
        : time.Value.ToUniversalTime();
}

using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.ReadModels.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features.Administration.Authorization;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ListOrganizationAuditRecords;

// From and To are an inclusive range on OccurredAt, in UTC
public record ListOrganizationAuditRecordsQuery(
    Guid OrganizationId,
    Guid CallerId,
    Guid? UserId,
    string? Action,
    DateTime? From,
    DateTime? To);

// An organization admin reads their organization's audit trail (ADR-0016), newest first. The trail is read
// only here: no use case updates or deletes an audit record (A3)
public class ListOrganizationAuditRecordsHandler(
    IMembershipRepository memberships,
    IAuditRecordQueries auditRecordQueries)
    : IQueryHandler<ListOrganizationAuditRecordsQuery, Result<IReadOnlyList<AuditRecordSummary>>>
{
    private readonly IMembershipRepository _memberships = memberships;
    private readonly IAuditRecordQueries _auditRecordQueries = auditRecordQueries;

    public async Task<Result<IReadOnlyList<AuditRecordSummary>>> Handle(
        ListOrganizationAuditRecordsQuery query,
        CancellationToken cancellationToken)
    {
        // Checked first: a caller who is not an organization admin there learns nothing about the trail
        if (!await _memberships.IsOrgAdminAsync(query.CallerId, query.OrganizationId, cancellationToken))
            return Result.Failure<IReadOnlyList<AuditRecordSummary>>(Errors.OrganizationNotFound);

        var records = await _auditRecordQueries.ListForOrganizationAsync(
            query.OrganizationId,
            ToFilter(query),
            Constants.MaxRecords,
            cancellationToken);

        return Result.Success<IReadOnlyList<AuditRecordSummary>>(records.Select(ToSummary).ToList());
    }

    private static AuditRecordFilter ToFilter(ListOrganizationAuditRecordsQuery query) =>
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

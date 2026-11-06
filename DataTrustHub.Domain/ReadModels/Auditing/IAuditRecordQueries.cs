namespace DataTrustHub.Domain.ReadModels.Auditing;

// The read side of the audit trail (ADR-0012 step 4, ADR-0016), apart from the append-only write port
// IAuditLog: both methods project straight to read models in SQL, newest first with the id as tie-break,
// cut at maxRecords, with the actor's email by left join. Nothing here updates or deletes a record (A3).
// No authorization here, and no business decision: the caller checks who may read first, and passes the
// scope and the maximum number of records
public interface IAuditRecordQueries
{
    // The records whose action is one of the given actions, e.g. the tenant-level actions
    Task<IReadOnlyList<AuditRecordEntry>> ListForActionsAsync(
        IReadOnlyCollection<string> actions,
        AuditRecordFilter filter,
        int maxRecords,
        CancellationToken cancellationToken);

    // The records of the organization
    Task<IReadOnlyList<AuditRecordEntry>> ListForOrganizationAsync(
        Guid organizationId,
        AuditRecordFilter filter,
        int maxRecords,
        CancellationToken cancellationToken);
}

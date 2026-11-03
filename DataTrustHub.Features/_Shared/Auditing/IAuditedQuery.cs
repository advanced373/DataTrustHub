namespace DataTrustHub.Features._Shared.Auditing;

// A query that is recorded in the audit trail (ADR-0025): the audit decorator appends one record per call.
// Queries are not audited unless they opt in with this marker
public interface IAuditedQuery
{
}

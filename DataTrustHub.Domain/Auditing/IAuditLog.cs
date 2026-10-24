namespace DataTrustHub.Domain.Auditing;

// The audit trail. Append-only: no use case updates or deletes an audit record (ADR-0016). The record is
// saved on its own, outside the command's unit of work, so it survives a rejected or rolled-back command
public interface IAuditLog
{
    Task AppendAsync(AuditRecord record, CancellationToken cancellationToken);
}

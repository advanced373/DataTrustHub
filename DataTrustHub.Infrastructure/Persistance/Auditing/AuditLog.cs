using DataTrustHub.Domain.Auditing;
using DataTrustHub.Infrastructure.Persistance.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Auditing;

// Writes each audit record through its own short-lived context, never the request's DContext: after a
// failed save that context still holds the rejected changes, which the audit save must neither retry nor
// be lost with (ADR-0016 point 5)
internal sealed class AuditLog(DbContextOptions<DContext> options) : IAuditLog
{
    public async Task AppendAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        await using var db = new DContext(options);
        db.AuditRecords.Add(record.ToDb());
        await db.SaveChangesAsync(cancellationToken);
    }
}

using DataTrustHub.Domain.Auditing;
using DataTrustHub.Infrastructure.Persistance.Model;
using Riok.Mapperly.Abstractions;

namespace DataTrustHub.Infrastructure.Persistance.Mapping;

// Write side only: the trail is appended, never loaded back as entities. The outcome is stored by name
[Mapper]
internal static partial class AuditRecordMapper
{
    internal static partial DbAuditRecord ToDb(this AuditRecord source);

    private static string ToName(AuditOutcome outcome) => outcome.ToString();
}

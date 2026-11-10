namespace DataTrustHub.Domain.ReadModels.Auditing;

// The optional filters of an audit trail, combined with AND; a null filter matches every record. From and
// To are an inclusive range on OccurredAt, already in UTC: the caller decides how a time without a kind is read
public sealed record AuditRecordFilter(Guid? ActorUserId, string? Action, DateTime? From, DateTime? To);

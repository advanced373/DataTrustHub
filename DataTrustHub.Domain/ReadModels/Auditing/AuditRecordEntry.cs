namespace DataTrustHub.Domain.ReadModels.Auditing;

// One audit record as an audit trail lists it. OccurredAt is in UTC; ActorEmail is the only thing about a
// user besides the actor's id, and it is null for an anonymous action or an account that no longer exists
public sealed record AuditRecordEntry(
    Guid Id,
    DateTime OccurredAt,
    Guid? ActorUserId,
    string? ActorEmail,
    string Action,
    string? TargetType,
    Guid? TargetId,
    string Outcome,
    string? ErrorCode);

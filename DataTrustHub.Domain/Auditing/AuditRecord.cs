namespace DataTrustHub.Domain.Auditing;

// One entry of the audit trail: who did what, on which target, in which organization, with which outcome
// (ADR-0016). It holds nothing else: never a command's payload, file content, emails, passwords, hashes or
// tokens. Targets and organizations are referenced by id only, and the record outlives them
public sealed class AuditRecord
{
    private const string ActionRequired = "An audit record needs an action.";
    private const string IncompleteTarget = "An audit record's target type and target id are set together.";
    private const string ErrorCodeOnSuccess = "A succeeded audit record has no error code.";
    private const string OccurredAtNotUtc = "An audit record's time is in UTC.";

    private AuditRecord(
        Guid id,
        DateTime occurredAt,
        Guid? actorUserId,
        string action,
        string? targetType,
        Guid? targetId,
        Guid? organizationId,
        AuditOutcome outcome,
        string? errorCode)
    {
        Id = id;
        OccurredAt = occurredAt;
        ActorUserId = actorUserId;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        OrganizationId = organizationId;
        Outcome = outcome;
        ErrorCode = errorCode;
    }

    public Guid Id { get; private set; }

    // UTC
    public DateTime OccurredAt { get; private set; }

    // Null for an anonymous action, e.g. register or log in
    public Guid? ActorUserId { get; private set; }

    // The command or access name, e.g. DeleteDataItem
    public string Action { get; private set; }

    public string? TargetType { get; private set; }

    public Guid? TargetId { get; private set; }

    public Guid? OrganizationId { get; private set; }

    public AuditOutcome Outcome { get; private set; }

    // The error code of a Rejected or Denied action, the exception type name of a Failed one
    public string? ErrorCode { get; private set; }

    // The arguments come from code, never from user input, so a broken rule is a programming error and throws
    public static AuditRecord Create(
        DateTime occurredAt,
        Guid? actorUserId,
        string action,
        string? targetType,
        Guid? targetId,
        Guid? organizationId,
        AuditOutcome outcome,
        string? errorCode)
    {
        if (occurredAt.Kind != DateTimeKind.Utc) throw new ArgumentException(OccurredAtNotUtc, nameof(occurredAt));
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException(ActionRequired, nameof(action));
        if ((targetType is null) != (targetId is null)) throw new ArgumentException(IncompleteTarget, nameof(targetType));
        if (outcome == AuditOutcome.Succeeded && errorCode is not null)
            throw new ArgumentException(ErrorCodeOnSuccess, nameof(errorCode));

        return new AuditRecord(
            Guid.NewGuid(), occurredAt, actorUserId, action, targetType, targetId, organizationId, outcome, errorCode);
    }
}

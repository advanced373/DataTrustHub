namespace DataTrustHub.Features._Shared.Auditing;

// What an audited command acts on: the type name (one of AuditTargetTypes) and the id
public sealed record AuditTarget(string Type, Guid Id);

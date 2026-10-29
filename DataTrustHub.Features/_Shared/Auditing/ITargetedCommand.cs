namespace DataTrustHub.Features._Shared.Auditing;

// A command whose target is known from the command itself; the audit decorator records it
public interface ITargetedCommand
{
    AuditTarget Target { get; }
}

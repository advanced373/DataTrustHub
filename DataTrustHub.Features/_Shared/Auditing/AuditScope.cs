namespace DataTrustHub.Features._Shared.Auditing;

// Scoped per request: a handler fills it when only the use case learns the target, the organization or the
// actor while it runs. What it holds overrides what the command itself declares and the caller's claims (ADR-0016)
public sealed class AuditScope
{
    public AuditTarget? Target { get; private set; }

    public Guid? OrganizationId { get; private set; }

    // Set when the use case learns who acts while it runs (a successful sign-in); wins over the caller's claim
    public Guid? ActorUserId { get; private set; }

    public void SetTarget(AuditTarget target) => Target = target;

    public void SetOrganization(Guid organizationId) => OrganizationId = organizationId;

    public void SetActor(Guid userId) => ActorUserId = userId;
}

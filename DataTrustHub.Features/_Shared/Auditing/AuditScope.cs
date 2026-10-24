namespace DataTrustHub.Features._Shared.Auditing;

// Scoped per request: a handler fills it when only the use case learns the target or the organization
// while it runs. What it holds overrides what the command itself declares (ADR-0016)
public sealed class AuditScope
{
    public AuditTarget? Target { get; private set; }

    public Guid? OrganizationId { get; private set; }

    public void SetTarget(AuditTarget target) => Target = target;

    public void SetOrganization(Guid organizationId) => OrganizationId = organizationId;
}

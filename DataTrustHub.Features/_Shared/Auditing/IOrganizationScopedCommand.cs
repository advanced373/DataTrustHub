namespace DataTrustHub.Features._Shared.Auditing;

// A command that runs inside one organization; the audit decorator records that organization
public interface IOrganizationScopedCommand
{
    Guid OrganizationId { get; }
}

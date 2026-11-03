namespace DataTrustHub.Features._Shared.Auditing;

// An audited query that runs inside one organization; the audit decorator records that organization
public interface IOrganizationScopedQuery
{
    Guid OrganizationId { get; }
}

namespace DataTrustHub.Domain.ReadModels.Organizations;

// The read side of organizations (ADR-0012 step 4): every method projects straight to a read model in SQL
public interface IOrganizationQueries
{
    // Every organization, ordered by name
    Task<IReadOnlyList<OrganizationEntry>> ListAsync(CancellationToken cancellationToken);

    // The organization, or null when no organization has this id
    Task<OrganizationEntry?> FindAsync(Guid organizationId, CancellationToken cancellationToken);
}

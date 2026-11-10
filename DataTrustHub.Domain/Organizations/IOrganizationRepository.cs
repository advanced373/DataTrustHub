namespace DataTrustHub.Domain.Organizations;

public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);

    // O1 for a rename: looks only at the other organizations, so a new spelling of its own name is no conflict
    Task<bool> NameExistsForOtherAsync(string name, Guid excludingOrganizationId, CancellationToken cancellationToken);

    void Add(Organization organization);

    Task UpdateAsync(Organization organization, CancellationToken cancellationToken);

    Task RemoveAsync(Organization organization, CancellationToken cancellationToken);
}

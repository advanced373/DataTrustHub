namespace DataTrustHub.Domain.Organizations;

public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);

    void Add(Organization organization);

    Task RemoveAsync(Organization organization, CancellationToken cancellationToken);
}

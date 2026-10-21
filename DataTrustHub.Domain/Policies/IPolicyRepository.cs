namespace DataTrustHub.Domain.Policies;

public interface IPolicyRepository
{
    Task<Policy?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> NameExistsInOrganizationAsync(string name, Guid organizationId, CancellationToken cancellationToken);

    Task<bool> AnyInOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    void Add(Policy policy);

    Task RemoveAsync(Policy policy, CancellationToken cancellationToken);
}

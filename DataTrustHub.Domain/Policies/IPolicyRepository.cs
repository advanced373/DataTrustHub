namespace DataTrustHub.Domain.Policies;

public interface IPolicyRepository
{
    Task<Policy?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> NameExistsInOrganizationAsync(string name, Guid organizationId, CancellationToken cancellationToken);

    // P2 for a rename: looks only at the organization's other policies, so a new spelling of its own name is no conflict
    Task<bool> NameExistsForOtherInOrganizationAsync(
        string name, Guid organizationId, Guid excludingPolicyId, CancellationToken cancellationToken);

    Task<bool> AnyInOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    // The ids of the organization's policies; used to revoke a former member's clearances there (C7)
    Task<IReadOnlyList<Guid>> ListIdsInOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    void Add(Policy policy);

    Task UpdateAsync(Policy policy, CancellationToken cancellationToken);

    Task RemoveAsync(Policy policy, CancellationToken cancellationToken);
}

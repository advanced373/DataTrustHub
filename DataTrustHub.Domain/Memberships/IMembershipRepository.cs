namespace DataTrustHub.Domain.Memberships;

public interface IMembershipRepository
{
    Task<Membership?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // The user's membership in the organization, or null when they are not a member
    Task<Membership?> GetAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken);

    Task<int> CountOrgAdminsAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Membership>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Membership>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    void Add(Membership membership);

    Task UpdateAsync(Membership membership, CancellationToken cancellationToken);

    Task RemoveAsync(Membership membership, CancellationToken cancellationToken);
}

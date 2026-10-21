namespace DataTrustHub.Domain.Clearances;

public interface IClearanceRepository
{
    Task<Clearance?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Clearance>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid userId, Guid policyId, CancellationToken cancellationToken);

    Task<bool> AnyForPolicyAsync(Guid policyId, CancellationToken cancellationToken);

    // Reuses a stored level with the same name and priority instead of storing a duplicate
    Task AddAsync(Clearance clearance, CancellationToken cancellationToken);

    Task RemoveAsync(Clearance clearance, CancellationToken cancellationToken);
}

namespace DataTrustHub.Domain.ReadModels.Policies;

// The read side of policies (ADR-0012 step 4): every method projects straight to a read model in SQL.
// No authorization here: the caller checks who may read first
public interface IPolicyQueries
{
    // The policies of the organization, ordered by name
    Task<IReadOnlyList<PolicyEntry>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    // The policy, or null when the organization has no policy with this id (a policy of another
    // organization is not found)
    Task<PolicyEntry?> FindAsync(Guid organizationId, Guid policyId, CancellationToken cancellationToken);
}

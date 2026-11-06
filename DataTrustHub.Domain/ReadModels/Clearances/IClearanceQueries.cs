namespace DataTrustHub.Domain.ReadModels.Clearances;

// The read side of clearances (ADR-0012 step 4): every method projects straight to a read model in SQL.
// No authorization here: the caller checks who may read first
public interface IClearanceQueries
{
    // The clearances on the policies of the organization, ordered by name; userId and policyId are optional
    // filters, so a policyId of another organization gives an empty list
    Task<IReadOnlyList<ClearanceEntry>> ListAsync(
        Guid organizationId,
        Guid? userId,
        Guid? policyId,
        CancellationToken cancellationToken);

    // The clearance, or null when no clearance with this id is on a policy of the organization
    Task<ClearanceEntry?> FindAsync(Guid organizationId, Guid clearanceId, CancellationToken cancellationToken);
}

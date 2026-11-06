namespace DataTrustHub.Domain.ReadModels.Memberships;

// The read side of memberships (ADR-0012 step 4): every method projects straight to a read model in SQL.
// No authorization here: the caller checks who may read first
public interface IMembershipQueries
{
    // The memberships of the organization, ordered by the email of their user
    Task<IReadOnlyList<OrganizationMember>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken);

    // The user's own memberships, ordered by organization name, then organization id; a membership whose
    // organization no longer exists is left out
    Task<IReadOnlyList<MyOrganization>> ListMyOrganizationsAsync(Guid userId, CancellationToken cancellationToken);
}

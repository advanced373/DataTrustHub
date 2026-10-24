using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Policies;

namespace DataTrustHub.Features.Administration.MembershipRules;

// Invariant C7 (ADR-0014): ending a membership in an organization revokes every clearance of that user on
// the organization's policies; clearances on other organizations' policies stay. It only stages the
// removals, so the use case saves them together with the end of the membership, in one transaction
internal static class ClearanceRevocation
{
    internal static async Task RevokeInOrganizationAsync(
        this IClearanceRepository clearances,
        IPolicyRepository policies,
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var policyIds = (await policies.ListIdsInOrganizationAsync(organizationId, cancellationToken)).ToHashSet();
        var userClearances = await clearances.ListForUserAsync(userId, cancellationToken);

        foreach (var clearance in userClearances.Where(clearance => policyIds.Contains(clearance.PolicyId)))
            await clearances.RemoveAsync(clearance, cancellationToken);
    }
}

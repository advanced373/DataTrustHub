using DataTrustHub.Domain.Memberships;

namespace DataTrustHub.Features.Administration.MembershipRules;

// Invariant M2 (ADR-0014): an organization always keeps at least one organization admin. It needs the
// other memberships, so the use case checks it before demoting or ending an organization admin's membership
internal static class OrgAdminRule
{
    internal const int MinimumOrgAdmins = 1;

    // True when the membership is the organization's only organization admin, so it cannot be demoted or ended
    internal static async Task<bool> IsLastOrgAdminAsync(
        this IMembershipRepository memberships,
        Membership membership,
        CancellationToken cancellationToken)
    {
        if (!membership.Role.IsOrgAdmin) return false;

        var orgAdminCount = await memberships.CountOrgAdminsAsync(membership.OrganizationId, cancellationToken);
        return orgAdminCount <= MinimumOrgAdmins;
    }
}

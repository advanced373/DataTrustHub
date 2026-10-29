using DataTrustHub.Domain.Memberships;

namespace DataTrustHub.Features.Administration.Authorization;

// Organization-scoped authorization (ADR-0014): decided per request from the caller's membership, not
// from the JWT, because a membership can change while a token is still valid. A caller who is not an
// organization admin gets the same answer as for a missing organization, so its existence is not revealed
internal static class OrganizationAuthorization
{
    internal static async Task<bool> IsOrgAdminAsync(
        this IMembershipRepository memberships,
        Guid callerId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var membership = await memberships.GetAsync(callerId, organizationId, cancellationToken);
        return membership is not null && membership.Role.IsOrgAdmin;
    }
}

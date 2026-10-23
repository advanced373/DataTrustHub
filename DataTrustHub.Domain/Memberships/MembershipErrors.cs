using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Memberships;

public static class MembershipErrors
{
    // Invariant M3; a conflict with the membership's current role, so the client gets 409
    public static readonly Error GuestCannotBeOrgAdmin = Error.Conflict(
        "Membership.GuestCannotBeOrgAdmin",
        "A guest cannot become an organization admin.");
}

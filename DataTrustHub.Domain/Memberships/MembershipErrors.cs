using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Memberships;

public static class MembershipErrors
{
    // Invariant M3; a conflict with the membership's current role, so the client gets 409
    public static readonly Error GuestRoleIsFixed = Error.Conflict(
        "Membership.GuestRoleIsFixed",
        "A guest stays a guest. End the membership and add the person again to change it.");
}

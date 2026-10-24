using DataTrustHub.Domain.Memberships;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ChangeMemberRole;

internal static class Constants
{
    internal const string EndpointRoute = "/organizations/{organizationId:guid}/members/{userId:guid}/role";
    internal const string EndpointTag = "Administration";
    internal const string RoleNotAssignableMessage = "The role must be Member or OrgAdmin.";

    // The guest role is fixed (M3): no membership changes to or from it
    internal static readonly IReadOnlyList<string> AssignableRoles = [MembershipRole.Member.Name, MembershipRole.OrgAdmin.Name];
}

internal static class Errors
{
    // Also the answer for a caller who is not an organization admin of the organization, so a caller
    // outside the organization does not learn that it exists
    internal static readonly Error OrganizationNotFound = Error.NotFound(
        "Organization.NotFound",
        "The organization was not found.");

    internal static readonly Error MembershipNotFound = Error.NotFound(
        "Membership.NotFound",
        "The user is not a member of this organization.");

    // Only when the validator did not run: the role is not a standard membership role (400)
    internal static readonly Error UnknownRole = new(
        "Membership.UnknownRole",
        "The role is not a known membership role.",
        ErrorType.Validation);

    // Invariant M2
    internal static readonly Error LastOrgAdmin = Error.Conflict(
        "Membership.LastOrgAdmin",
        "The organization must keep at least one organization admin. Appoint another one first.");
}

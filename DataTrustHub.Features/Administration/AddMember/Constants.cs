using DataTrustHub.Domain.Memberships;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.AddMember;

internal static class Constants
{
    internal const string EndpointRoute = "/organizations/{organizationId:guid}/members";
    internal const string EndpointTag = "Administration";
    internal const string RoleNotAddableMessage = "The role must be Member or Guest.";

    // Organization admins are appointed, not added (ADR-0014)
    internal static readonly IReadOnlyList<string> AddableRoles = [MembershipRole.Member.Name, MembershipRole.Guest.Name];
}

internal static class Errors
{
    // Also the answer for a caller who is not an organization admin of the organization, so a caller
    // outside the organization does not learn that it exists
    internal static readonly Error OrganizationNotFound = Error.NotFound(
        "Organization.NotFound",
        "The organization was not found.");

    internal static readonly Error UserNotFound = Error.NotFound(
        "Membership.UserNotFound",
        "The specified user does not exist.");

    internal static readonly Error AlreadyMember = Error.Conflict(
        "Membership.AlreadyMember",
        "The user is already a member of this organization.");
}

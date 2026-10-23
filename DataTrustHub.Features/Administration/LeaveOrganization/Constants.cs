using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.LeaveOrganization;

internal static class Constants
{
    internal const string EndpointRoute = "/organizations/{organizationId:guid}/membership";
    internal const string EndpointTag = "Administration";
}

internal static class Errors
{
    // Also the answer for a caller who is not a member of the organization, so a caller outside the
    // organization does not learn that it exists
    internal static readonly Error OrganizationNotFound = Error.NotFound(
        "Organization.NotFound",
        "The organization was not found.");

    // Invariant M2
    internal static readonly Error LastOrgAdmin = Error.Conflict(
        "Membership.LastOrgAdmin",
        "The organization must keep at least one organization admin. Appoint another one first.");
}

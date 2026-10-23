using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ListMembers;

internal static class Constants
{
    internal const string EndpointRoute = "/organizations/{organizationId:guid}/members";
    internal const string EndpointTag = "Administration";
}

internal static class Errors
{
    // Also the answer for a caller who is not an organization admin of the organization, so a caller
    // outside the organization does not learn that it exists
    internal static readonly Error OrganizationNotFound = Error.NotFound(
        "Organization.NotFound",
        "The organization was not found.");
}

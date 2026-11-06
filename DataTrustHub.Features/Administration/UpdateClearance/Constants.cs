using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.UpdateClearance;

internal static class Constants
{
    internal const string EndpointRoute = "/organizations/{organizationId:guid}/clearances/{clearanceId:guid}";
    internal const string EndpointTag = "Administration";
    internal const int MaxNameLength = 256;
    internal const int MinPriority = 0;
}

internal static class Errors
{
    // Also the answer for a caller who is not an organization admin of the organization, so a caller
    // outside the organization does not learn that it exists
    internal static readonly Error OrganizationNotFound = Error.NotFound(
        "Organization.NotFound",
        "The organization was not found.");

    // Also the answer for a clearance on a policy of another organization, so its existence is not revealed
    internal static readonly Error ClearanceNotFound = Error.NotFound(
        "Clearance.NotFound",
        "The clearance was not found.");

    // Invariant C8: changing one's own clearance could raise one's own access
    internal static readonly Error CannotChangeOwn = Error.Conflict(
        "Clearance.CannotChangeOwn",
        "An organization admin cannot change their own clearance. Another organization admin must change it.");
}

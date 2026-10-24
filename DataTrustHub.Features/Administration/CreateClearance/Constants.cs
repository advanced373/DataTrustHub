using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.CreateClearance;

internal static class Constants
{
    internal const string EndpointRoute = "/organizations/{organizationId:guid}/clearances";
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

    internal static readonly Error UserNotFound = Error.NotFound(
        "Clearance.UserNotFound",
        "The specified user does not exist.");

    // Also the answer for a policy of another organization, so its existence is not revealed
    internal static readonly Error PolicyNotFound = Error.NotFound(
        "Clearance.PolicyNotFound",
        "The specified policy does not exist.");

    internal static readonly Error UserNotMember = Error.Conflict(
        "Clearance.UserNotMember",
        "The user is not a member or guest of this organization.");

    internal static readonly Error CannotGrantSelf = Error.Conflict(
        "Clearance.CannotGrantSelf",
        "An organization admin cannot grant a clearance to themselves. Another organization admin must grant it.");

    internal static readonly Error AlreadyGranted = Error.Conflict(
        "Clearance.AlreadyGranted",
        "The user already has a clearance for this policy.");
}

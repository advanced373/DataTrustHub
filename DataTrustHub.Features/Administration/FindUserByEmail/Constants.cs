using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.FindUserByEmail;

internal static class Constants
{
    internal const string EndpointRoute = "/organizations/{organizationId:guid}/users/find-by-email";
    internal const string EndpointTag = "Administration";

    // Same value as the account and registration validators
    internal const int MaxEmailLength = 256;
}

internal static class Errors
{
    // Also the answer for a caller who is not an organization admin of the organization, so a caller
    // outside the organization does not learn that it exists
    internal static readonly Error OrganizationNotFound = Error.NotFound(
        "Organization.NotFound",
        "The organization was not found.");

    internal static readonly Error UserNotFound = Error.NotFound(
        "User.NotFound",
        "No user has this email address.");
}

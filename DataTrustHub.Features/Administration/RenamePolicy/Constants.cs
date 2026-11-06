using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.RenamePolicy;

internal static class Constants
{
    internal const string EndpointRoute = "/organizations/{organizationId:guid}/policies/{policyId:guid}";
    internal const string EndpointTag = "Administration";
    internal const int MaxNameLength = 256;
}

internal static class Errors
{
    // Also the answer for a caller who is not an organization admin of the organization, so a caller
    // outside the organization does not learn that it exists
    internal static readonly Error OrganizationNotFound = Error.NotFound(
        "Organization.NotFound",
        "The organization was not found.");

    // Also the answer for a policy of another organization, so its existence is not revealed
    internal static readonly Error PolicyNotFound = Error.NotFound(
        "Policy.NotFound",
        "The policy was not found.");

    // Invariant P2
    internal static readonly Error PolicyNameAlreadyInUse = Error.Conflict(
        "Policy.NameAlreadyInUse",
        "A policy with this name already exists for the given organization.");
}

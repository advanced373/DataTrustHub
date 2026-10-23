using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeletePolicy;

internal static class Constants
{
    internal const string EndpointRoute = "/organizations/{organizationId:guid}/policies/{policyId:guid}";
    internal const string EndpointTag = "Administration";
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

    internal static readonly Error PolicyHasClearances = Error.Conflict(
        "Policy.HasClearances",
        "The policy still has clearances. Delete its clearances first.");
}

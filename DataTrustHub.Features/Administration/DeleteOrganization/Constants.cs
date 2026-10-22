using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeleteOrganization;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/organizations/{id:guid}";
    internal const string EndpointTag = "Administration";
}

internal static class Errors
{
    internal static readonly Error OrganizationNotFound = Error.NotFound(
        "Organization.NotFound",
        "The organization was not found.");

    internal static readonly Error OrganizationHasPolicies = Error.Conflict(
        "Organization.HasPolicies",
        "The organization still has policies. Delete its policies first.");
}

using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.GetOrganization;

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
}

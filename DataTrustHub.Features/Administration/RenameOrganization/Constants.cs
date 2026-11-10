using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.RenameOrganization;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/organizations/{organizationId:guid}";
    internal const string EndpointTag = "Administration";
    internal const int MaxNameLength = 256;
}

internal static class Errors
{
    internal static readonly Error OrganizationNotFound = Error.NotFound(
        "Organization.NotFound",
        "The organization was not found.");

    // Invariant O1
    internal static readonly Error OrganizationNameAlreadyInUse = Error.Conflict(
        "Organization.NameAlreadyInUse",
        "An organization with this name already exists.");
}

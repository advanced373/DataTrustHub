using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.CreateOrganization;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/organizations";
    internal const string EndpointTag = "Administration";
    internal const int MaxNameLength = 256;
}

internal static class Errors
{
    internal static readonly Error OrganizationNameAlreadyInUse = Error.Conflict(
        "Organization.NameAlreadyInUse",
        "An organization with this name already exists.");
}

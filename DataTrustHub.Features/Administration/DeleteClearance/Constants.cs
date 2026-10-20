using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeleteClearance;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/clearances/{id:guid}";
    internal const string EndpointTag = "Administration";
}

internal static class Errors
{
    internal static readonly Error ClearanceNotFound = Error.NotFound(
        "Clearance.NotFound",
        "The clearance was not found.");
}

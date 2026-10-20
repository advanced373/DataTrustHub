using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.GetGroup;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/groups/{id:guid}";
    internal const string EndpointTag = "Administration";
}

internal static class Errors
{
    internal static readonly Error GroupNotFound = Error.NotFound(
        "Group.NotFound",
        "The group was not found.");
}

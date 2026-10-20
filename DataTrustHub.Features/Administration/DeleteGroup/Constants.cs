using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeleteGroup;

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

    internal static readonly Error GroupHasPolicies = Error.Conflict(
        "Group.HasPolicies",
        "The group still has policies. Delete its policies first.");
}

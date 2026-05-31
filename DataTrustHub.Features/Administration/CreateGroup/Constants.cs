using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.CreateGroup;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/groups";
    internal const string EndpointTag = "Administration";
    internal const int MaxNameLength = 256;
}

internal static class Errors
{
    internal static readonly Error GroupNameAlreadyInUse = Error.Conflict(
        "Group.NameAlreadyInUse",
        "A group with this name already exists.");
}

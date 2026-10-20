using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.GetPolicy;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/policies/{id:guid}";
    internal const string EndpointTag = "Administration";
}

internal static class Errors
{
    internal static readonly Error PolicyNotFound = Error.NotFound(
        "Policy.NotFound",
        "The policy was not found.");
}

using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeletePolicy;

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

    internal static readonly Error PolicyHasClearances = Error.Conflict(
        "Policy.HasClearances",
        "The policy still has clearances. Delete its clearances first.");
}

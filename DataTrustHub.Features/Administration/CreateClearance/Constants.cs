using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.CreateClearance;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/clearances";
    internal const string EndpointTag = "Administration";
    internal const int MaxNameLength = 256;
    internal const int MinPriority = 0;
}

internal static class Errors
{
    internal static readonly Error UserNotFound = Error.NotFound(
        "Clearance.UserNotFound",
        "The specified user does not exist.");

    internal static readonly Error PolicyNotFound = Error.NotFound(
        "Clearance.PolicyNotFound",
        "The specified policy does not exist.");

    internal static readonly Error AlreadyGranted = Error.Conflict(
        "Clearance.AlreadyGranted",
        "The user already has a clearance for this policy.");
}

using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.CreatePolicy;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/policies";
    internal const string EndpointTag = "Administration";
    internal const int MaxNameLength = 256;
}

internal static class Errors
{
    internal static readonly Error PolicyNameAlreadyInUse = Error.Conflict(
        "Policy.NameAlreadyInUse",
        "A policy with this name already exists for the given organization.");

    internal static readonly Error OrganizationNotFound = Error.NotFound(
        "Policy.OrganizationNotFound",
        "The specified organization does not exist.");
}

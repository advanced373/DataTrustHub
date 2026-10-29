using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.GetAccount;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/accounts/{id:guid}";
    internal const string EndpointTag = "Administration";
}

internal static class Errors
{
    internal static readonly Error AccountNotFound = Error.NotFound(
        "Account.NotFound",
        "The account was not found.");
}

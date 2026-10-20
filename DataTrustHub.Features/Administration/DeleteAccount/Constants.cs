using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.DeleteAccount;

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

    internal static readonly Error CannotDeleteOwnAccount = Error.Conflict(
        "Account.CannotDeleteOwnAccount",
        "Administrators cannot delete their own account.");
}

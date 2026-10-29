using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.CreateAccount;

internal static class Constants
{
    internal const string EndpointRoute = "/admin/accounts";
    internal const string EndpointTag = "Administration";
    internal const int MinPasswordLength = 8;
    internal const int MaxEmailLength = 256;
}

internal static class Errors
{
    internal static readonly Error EmailAlreadyInUse = Error.Conflict(
        "Account.EmailAlreadyInUse",
        "A user with this email address already exists.");
}
